import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { Trust } from '../../core/admin.models';
import { TrustDialogComponent } from './trust-dialog.component';
import { TrustsListComponent } from './trusts-list.component';

function trust(overrides: Partial<Trust> = {}): Trust {
  return {
    id: 't1',
    localityId: 'loc1',
    name: 'Hiran Magri Samaaj Trust',
    status: 'Active',
    createdAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

const LOCALITIES = [
  { id: 'loc1', cityId: 'udr', name: 'Hiran Magri', isActive: true },
  { id: 'loc9', cityId: 'udr', name: 'Surajpole', isActive: true },
];
const CITIES = [{ id: 'udr', countryId: 'in', stateId: null, name: 'Udaipur', isActive: true }];

describe('TrustsListComponent', () => {
  let fixture: ComponentFixture<TrustsListComponent>;
  let http: HttpTestingController;

  const click = { currentTarget: null } as unknown as MouseEvent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TrustsListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(TrustsListComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function settleLocalities() {
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush(LOCALITIES);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush(CITIES);
  }

  function load(trusts: Trust[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/trust/trusts').flush(trusts);
    settleLocalities();
    fixture.detectChanges();
  }

  function dialog(): TrustDialogComponent {
    return fixture.debugElement.query((el) => el.componentInstance instanceof TrustDialogComponent)
      .componentInstance as TrustDialogComponent;
  }

  it('reads a 404 as the module being off', () => {
    fixture.detectChanges();
    http.expectOne('/v1/trust/trusts').flush({}, { status: 404, statusText: 'Not Found' });
    settleLocalities();
    fixture.detectChanges();

    expect(text()).toContain('does not run the Trust module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('narrows the list to a locality, or to the whole Samaaj, from the Locality filter', () => {
    load([trust(), trust({ id: 't2', localityId: 'loc9', name: 'Surajpole Trust' })]);

    const rows = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr')].map((r) =>
      r.querySelector('b')?.textContent,
    );

    expect(rows()).toEqual(['Hiran Magri Samaaj Trust', 'Surajpole Trust']);

    fixture.componentInstance.localityFilter.set('loc9');
    fixture.detectChanges();
    expect(rows()).toEqual(['Surajpole Trust']);

    // A Trust always has a locality, so the whole-Samaaj view is empty, and says so.
    fixture.componentInstance.localityFilter.set('whole-samaaj');
    fixture.detectChanges();
    expect(rows()).toEqual([]);
    expect(text()).toContain('Nothing here for that locality');
  });

  it('says when there are none yet', () => {
    load([]);

    expect(text()).toContain('None yet');
  });

  it('names each Trust’s locality rather than printing its id', () => {
    load([trust()]);

    expect(text()).toContain('Hiran Magri Samaaj Trust');
    expect(text()).toContain('Hiran Magri, Udaipur');
    expect(text()).not.toContain('loc1');
  });

  it('sets a Trust up in a popup, needing a name and a locality', () => {
    load([]);

    fixture.componentInstance.openCreate(click);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();

    const form = dialog();

    expect(form.canSave()).toBe(false);
    form.name = 'Surajpole Samaaj Trust';
    expect(form.canSave()).toBe(false);
    form.localityId = 'loc9';
    expect(form.canSave()).toBe(true);

    form.save();

    const call = http.expectOne('/v1/trust/trusts');

    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual({ localityId: 'loc9', name: 'Surajpole Samaaj Trust' });

    call.flush(trust({ id: 't2', name: 'Surajpole Samaaj Trust', localityId: 'loc9' }));
    http.expectOne('/v1/trust/trusts').flush([trust({ id: 't2', localityId: 'loc9' })]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();
    expect(text()).toContain('Surajpole Samaaj Trust is set up.');
  });

  it('corrects a Trust’s name in the same popup, showing but not offering its locality', () => {
    load([trust()]);

    fixture.componentInstance.openEdit(trust(), click);
    fixture.detectChanges();

    const form = dialog();
    const locality = fixture.nativeElement.querySelector('#trust-locality') as HTMLInputElement;

    expect(form.name).toBe('Hiran Magri Samaaj Trust');
    expect(locality.disabled).toBe(true);
    expect(locality.value).toBe('Hiran Magri, Udaipur');

    form.name = 'Hiran Magri Jain Samaaj Trust';
    form.save();

    const call = http.expectOne('/v1/trust/trusts/t1');

    expect(call.request.method).toBe('PATCH');
    expect(call.request.body).toEqual({ name: 'Hiran Magri Jain Samaaj Trust' });

    call.flush(trust({ name: 'Hiran Magri Jain Samaaj Trust' }));
    http.expectOne('/v1/trust/trusts').flush([trust({ name: 'Hiran Magri Jain Samaaj Trust' })]);
    fixture.detectChanges();

    expect(text()).toContain('is saved');
  });

  it('keeps the popup open, with what was typed, when the service refuses', () => {
    load([]);

    fixture.componentInstance.openCreate(click);
    fixture.detectChanges();

    const form = dialog();
    form.name = 'Second Trust';
    form.localityId = 'loc1';
    form.save();

    http.expectOne('/v1/trust/trusts').flush(
      { title: 'This Samaaj already has a Trust for that locality.' },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"] [role="alert"]')).not.toBeNull();
    expect(form.name).toBe('Second Trust');
  });

  it('closes on Escape', () => {
    load([]);

    fixture.componentInstance.openCreate(click);
    fixture.detectChanges();

    const box = fixture.nativeElement.querySelector('[role="dialog"]') as HTMLElement;
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();
  });

  it('deactivates and reactivates a Trust', () => {
    load([trust()]);

    fixture.componentInstance.setStatus(trust(), 'Inactive');

    http.expectOne('/v1/trust/trusts/t1/status').flush(trust({ status: 'Inactive' }));
    http.expectOne('/v1/trust/trusts').flush([trust({ status: 'Inactive' })]);
    fixture.detectChanges();

    expect(text()).toContain('is deactivated');
  });
});
