import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { Trust, TrustElection, TrustMember } from '../../core/admin.models';
import { TrustDetailComponent } from './trust-detail.component';

const TRUST_ID = 't1';

function trust(overrides: Partial<Trust> = {}): Trust {
  return {
    id: TRUST_ID,
    localityId: 'loc1',
    name: 'Hiran Magri Samaaj Trust',
    status: 'Active',
    createdAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

function member(overrides: Partial<TrustMember> = {}): TrustMember {
  return {
    id: 'tm1',
    trustId: TRUST_ID,
    memberId: 'm1',
    positionTitle: null,
    status: 'Active',
    addedBy: 'admin1',
    addedAt: '2026-09-02T00:00:00Z',
    removedBy: null,
    removedAt: null,
    ...overrides,
  };
}

const MEMBERS = [{ id: 'm1', fullName: 'Diya Jain' }];

describe('TrustDetailComponent', () => {
  let fixture: ComponentFixture<TrustDetailComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TrustDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', TRUST_ID]]) } },
        },
      ],
    });

    fixture = TestBed.createComponent(TrustDetailComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(members: TrustMember[] = [], elections: TrustElection[] = []) {
    fixture.detectChanges();
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}`).flush(trust());
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).flush(members);
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/elections`).flush(elections);
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    fixture.detectChanges();
  }

  it('opens on the roster, and loads a money tab only when it is opened', () => {
    load([member()]);

    const tabs = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="tab"]')];

    expect(tabs.map((t) => t.textContent?.trim())).toEqual([
      'Roster & elections',
      'Donations',
      'Expenses',
      'Collections',
    ]);
    expect(text()).toContain('Roster');

    http.expectNone(`/v1/trust/trusts/${TRUST_ID}/collections`);

    tabs[3]!.click();
    fixture.detectChanges();

    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/collections`).flush([]);
    fixture.detectChanges();

    expect(text()).toContain('Nothing is owed to this Trust yet');
    expect(text()).not.toContain('Add a trustee');
  });

  it('reads a 404 as no such Trust', () => {
    fixture.detectChanges();
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}`).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such Trust');
  });

  it('shows the roster with resolved names', () => {
    load([member()]);

    expect(text()).toContain('Diya Jain');
    expect(text()).toContain('1 trustee');
  });

  it('says when nobody has been added yet', () => {
    load([]);

    expect(text()).toContain('Nobody added yet');
  });

  it('adds a searched member to the roster', () => {
    load([]);

    const component = fixture.componentInstance;
    component.searchTerm = 'Diya';
    component.search();

    http.expectOne('/v1/members?skip=0&take=20&term=Diya').flush({
      items: [{ id: 'm1', fullName: 'Diya Jain' }],
      hasMore: false,
    });
    fixture.detectChanges();

    expect(text()).toContain('Diya Jain');

    component.selectMember({ id: 'm1', fullName: 'Diya Jain' } as never);
    fixture.detectChanges();

    expect(text()).toContain('Adding Diya Jain');

    component.addMember({ id: 'm1', fullName: 'Diya Jain' } as never);

    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).flush(member());
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).flush([member()]);
    fixture.detectChanges();

    expect(text()).toContain('added to the roster');
  });

  it('removes a member with no confirmation step', () => {
    load([member()]);

    fixture.componentInstance.removeMember(member());

    http
      .expectOne(`/v1/trust/trusts/${TRUST_ID}/members/tm1`)
      .flush(member({ status: 'Removed', removedBy: 'admin1', removedAt: '2026-09-05T00:00:00Z' }));
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).flush([
      member({ status: 'Removed' }),
    ]);
    fixture.detectChanges();

    expect(text()).toContain('off the roster');
  });

  it('only allows opening an election once the windows are valid and forward-ordered', () => {
    load([]);

    const component = fixture.componentInstance;
    component.positionTitle = 'Treasurer';
    component.nominationStartAt = '2026-10-01T00:00';
    component.nominationEndAt = '2026-10-10T00:00';
    component.votingStartAt = '2026-10-05T00:00';
    component.votingEndAt = '2026-10-20T00:00';

    // Voting starts before nominations close.
    expect(component.canOpen()).toBe(false);

    component.votingStartAt = '2026-10-11T00:00';
    expect(component.canOpen()).toBe(true);
  });
});
