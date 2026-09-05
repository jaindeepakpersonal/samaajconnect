import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { PrivacyComponent } from './privacy.component';
import { ConsentNotice, ConsentState, IdentityExport } from './privacy.models';

const NOTICE = '/v1/identity/consent-notice';
const IDENTITY = '/v1/identity/me/data-export';
const MEMBERS = '/v1/members/me/data-export';
const AUDIT = '/v1/audit/me/data-export';
const ERASE = '/v1/identity/me/erase';

function notice(): ConsentNotice {
  return {
    version: '2026-08-28.1',
    items: [
      {
        purpose: 'Membership',
        title: 'Membership and the directory',
        description: 'Holding an account and appearing in the Samaaj member directory.',
        required: true,
      },
      {
        purpose: 'Communications',
        title: 'Samaaj communications',
        description: 'Announcements, event notices and other Samaaj communication.',
        required: false,
      },
      {
        purpose: 'CrossSamaajDirectory',
        title: 'Other Samaaj on the platform',
        description: 'Showing your profile to members of other Samaaj.',
        required: false,
      },
    ],
  };
}

function consents(overrides: Partial<Record<string, boolean>> = {}): ConsentState[] {
  const granted = { Membership: true, Communications: true, CrossSamaajDirectory: false, ...overrides };

  return Object.entries(granted).map(([purpose, isGranted]) => ({
    purpose,
    granted: isGranted as boolean,
    noticeVersion: '2026-08-28.1',
    decidedAt: '2026-06-01T09:00:00Z',
  }));
}

function identityExport(overrides: Partial<IdentityExport> = {}): IdentityExport {
  return {
    exportedAt: '2026-08-29T12:00:00Z',
    service: 'identity-tenant-service',
    account: {
      userId: 'u1',
      tenantId: 't1',
      tenantSlug: 'mahavir-samaj',
      mobileOrEmail: 'member@example.com',
      fullName: 'Deepak Jain',
      status: 'Active',
      isContactVerified: true,
      createdAt: '2026-06-01T09:00:00Z',
      lastLoginAt: '2026-08-29T08:00:00Z',
      roles: ['Member'],
    },
    consentHistory: [],
    currentConsents: consents(),
    processingPurposes: notice().items,
    heldElsewhere: ['member-family-service', 'audit-notification-service'],
    ...overrides,
  };
}

describe('PrivacyComponent', () => {
  let fixture: ComponentFixture<PrivacyComponent>;
  let component: PrivacyComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PrivacyComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(PrivacyComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);

    // The test DOM implements neither of these, and a click on a real anchor
    // would try to navigate. Stubbed rather than worked around in the
    // component, so that what ships is what a browser actually runs.
    URL.createObjectURL = vi.fn(() => 'blob:test');
    URL.revokeObjectURL = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  });

  afterEach(() => http.verify());

  function load(held: IdentityExport = identityExport()): void {
    fixture.detectChanges();

    http.expectOne(NOTICE).flush(notice());
    http.expectOne(IDENTITY).flush(held);

    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  it('shows each purpose in the words the member was shown', () => {
    load();

    expect(text()).toContain('Membership and the directory');
    expect(text()).toContain('Samaaj communications');
    expect(text()).toContain('Notice version 2026-08-28.1');
  });

  it('marks where each consent currently stands', () => {
    load();

    expect(component.isGranted(notice().items[1])).toBe(true);
    expect(component.isGranted(notice().items[2])).toBe(false);
    expect(text()).toContain('Agreed');
    expect(text()).toContain('Withdrawn');
  });

  it('withdraws without asking the member to confirm', () => {
    // Section 6(4): withdrawing must be as easy as giving, and giving was a
    // tick during registration. A confirmation step would make it harder.
    load();

    component.withdraw(notice().items[1]);

    const request = http.expectOne(`/v1/identity/me/consents/Communications/withdraw`);

    expect(request.request.method).toBe('POST');

    request.flush(consents({ Communications: false }));
    fixture.detectChanges();

    expect(component.isGranted(notice().items[1])).toBe(false);
  });

  it('does not offer to withdraw the purpose the account cannot exist without', () => {
    load();

    // Not a disabled button either: withdrawing membership is not temporarily
    // unavailable, it means erasing the account, and the screen says so.
    expect(text()).toContain('Erasing your account, below, is how you withdraw it');
  });

  it('says what is done with the data, not only what is held', () => {
    // Section 11 asks for a summary of the processing activities as well as of
    // the data itself.
    load();

    expect(text()).toContain('What is done with it');
    expect(text()).toContain('Announcements, event notices');
  });

  it('lists the other services that hold data rather than running them together', () => {
    // Each entry names a service and what it holds, and those phrases carry
    // their own commas - "your profile, family and children" - so joining them
    // into one sentence produced an unparseable run-on.
    load(
      identityExport({
        heldElsewhere: [
          'member-family-service: your profile, family and children',
          'audit-notification-service: your notifications, and the audit record of actions taken',
        ],
      }),
    );

    const items = Array.from(
      fixture.nativeElement.querySelectorAll('li') as NodeListOf<HTMLLIElement>,
    ).map((item) => item.textContent?.trim() ?? '');

    expect(items).toContain('member-family-service: your profile, family and children');
    expect(text()).not.toContain('family and children, audit-notification-service');
  });

  it('assembles the copy from all three services', () => {
    load();

    component.download();

    http.expectOne(IDENTITY).flush(identityExport());
    http.expectOne(MEMBERS).flush({ service: 'member-family-service', members: [] });
    http.expectOne(AUDIT).flush({ service: 'audit-notification-service', events: [] });

    fixture.detectChanges();

    expect(component.exportReady()).toBe(true);
    expect(component.exportError()).toBeNull();
  });

  it('still delivers a copy when one service has nothing for this member', () => {
    // A member with no family record 404s there. A partial copy delivered is
    // worth more than a complete one refused.
    load();

    component.download();

    http.expectOne(IDENTITY).flush(identityExport());
    http.expectOne(MEMBERS).flush(null, { status: 404, statusText: 'Not Found' });
    http.expectOne(AUDIT).flush({ service: 'audit-notification-service', events: [] });

    fixture.detectChanges();

    expect(component.exportReady()).toBe(true);
    expect(component.exportError()).toBeNull();
  });

  it('says so when the copy was gathered but the browser would not save it', () => {
    // Two different failures, and the member needs them told apart: this one
    // means the data exists and the download was blocked, which a button that
    // appears to do nothing would never explain.
    load();

    URL.createObjectURL = vi.fn(() => {
      throw new Error('downloads blocked');
    });

    component.download();

    http.expectOne(IDENTITY).flush(identityExport());
    http.expectOne(MEMBERS).flush({ service: 'member-family-service', members: [] });
    http.expectOne(AUDIT).flush({ service: 'audit-notification-service', events: [] });

    fixture.detectChanges();

    expect(component.exportReady()).toBe(false);
    expect(component.exportError()).toContain('would not save the file');
  });

  it('asks for a password before erasing, and does not erase until it has one', () => {
    load();

    component.askToConfirm();
    fixture.detectChanges();

    expect(component.confirming()).toBe(true);

    // Nothing submitted while the field is empty; http.verify() proves it.
    component.erase();
  });

  it('keeps its trigger in the DOM rather than replacing it with the panel', () => {
    // A confirmation that swaps out its own trigger drops keyboard focus to
    // the document body - the finding three admin screens already shared
    // (WCAG 2.4.3), and family.component.ts's own leave-household panel cites
    // it explicitly. This screen's erasure trigger did not, despite being the
    // single most irreversible action on the platform.
    load();

    component.askToConfirm();
    fixture.detectChanges();

    const trigger = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Erase my account') as HTMLButtonElement | undefined;

    expect(trigger).toBeDefined();
    expect(trigger?.getAttribute('aria-expanded')).toBe('true');
  });

  it('announces why it asks for a password to a screen reader, not only sighted members', () => {
    // The same WCAG 4.1.3 gap as the admin panel's own password-confirmation
    // screens: nothing else moves focus when this text appears, so a screen
    // reader user hears nothing unless it is a live region.
    load();

    component.askToConfirm();
    fixture.detectChanges();

    const warning = (fixture.nativeElement as HTMLElement).querySelector(
      '.card p[role="status"]',
    );

    expect(warning?.textContent).toContain('deliberate one');
  });

  it('keeps the panel open on a wrong password rather than ending the session', () => {
    // A wrong password answers 403 Auth.StepUpFailed and not 401, because the
    // interceptor renews on a 401 and retries - which here would submit the
    // erasure a second time because somebody mistyped.
    load();

    component.askToConfirm();
    component.password = 'wrong-password';
    component.erase();

    http.expectOne(ERASE).flush(
      { title: 'Auth.StepUpFailed', status: 403, detail: 'That password is not right.' },
      { status: 403, statusText: 'Forbidden' },
    );

    fixture.detectChanges();

    expect(component.erased()).toBeNull();
    expect(component.confirming()).toBe(true);
    expect(component.eraseError()).not.toBeNull();
    expect(component.password).toBe('');
  });

  it('says what was kept as well as what was erased', () => {
    // A member told only "done" has no way to know an audit record survives.
    load();

    component.askToConfirm();
    component.password = 'a-long-enough-password';
    component.erase();

    http.expectOne(ERASE).flush({
      userId: 'u1',
      erasedAt: '2026-08-29T12:30:00Z',
      whatWasErased: ['Your profile', 'Your household link'],
      whatIsKeptAndWhy: ['De-identified audit rows, required by law'],
    });

    fixture.detectChanges();

    expect(text()).toContain('Your profile');
    expect(text()).toContain('De-identified audit rows, required by law');
    expect(text()).toContain('What is kept, and why');
  });

  it('shows nothing else once the account is gone', () => {
    load();

    component.askToConfirm();
    component.password = 'a-long-enough-password';
    component.erase();

    http.expectOne(ERASE).flush({
      userId: 'u1',
      erasedAt: '2026-08-29T12:30:00Z',
      whatWasErased: ['Your profile'],
      whatIsKeptAndWhy: [],
    });

    fixture.detectChanges();

    // The consent controls and the download button no longer apply to an
    // account that does not exist. Asserted on things unique to those
    // sections - the page subtitle says "What you agreed to" and stays put.
    expect(text()).not.toContain('Download my data');
    expect(text()).not.toContain('Notice version');
    expect(text()).not.toContain('Erase my account');
    expect(text()).toContain('Close and sign out');
  });
});
