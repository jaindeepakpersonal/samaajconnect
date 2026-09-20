import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { Trust } from '../../core/admin.models';
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

describe('TrustsListComponent', () => {
  let fixture: ComponentFixture<TrustsListComponent>;
  let http: HttpTestingController;

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

  function load(trusts: Trust[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/trust/trusts').flush(trusts);
    http.expectOne('/v1/master-data/lookup-items?category=Country&includeInactive=false').flush(
      [],
    );
    fixture.detectChanges();
  }

  it('reads a 404 as the module being off', () => {
    fixture.detectChanges();
    http.expectOne('/v1/trust/trusts').flush({}, { status: 404, statusText: 'Not Found' });
    http.expectOne('/v1/master-data/lookup-items?category=Country&includeInactive=false').flush(
      [],
    );
    fixture.detectChanges();

    expect(text()).toContain('does not run the Trust module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('says when there are none yet', () => {
    load([]);

    expect(text()).toContain('None yet');
  });

  it('lists a Trust with its status', () => {
    load([trust()]);

    expect(text()).toContain('Hiran Magri Samaaj Trust');
    expect(text()).toContain('Active');
  });

  it('disables Create until a name and a locality are both chosen', () => {
    load([]);

    const component = fixture.componentInstance;

    expect(component.canCreate()).toBe(false);

    component.name = 'New Trust';
    expect(component.canCreate()).toBe(false);

    component.localityId = 'loc9';
    expect(component.canCreate()).toBe(true);
  });

  it('creates a Trust and reports it', () => {
    load([]);

    const component = fixture.componentInstance;

    component.name = 'Surajpole Samaaj Trust';
    component.localityId = 'loc9';
    component.create();

    http
      .expectOne('/v1/trust/trusts')
      .flush(trust({ id: 't2', name: 'Surajpole Samaaj Trust', localityId: 'loc9' }));

    http.expectOne('/v1/trust/trusts').flush([trust({ id: 't2', localityId: 'loc9' })]);
    fixture.detectChanges();

    expect(text()).toContain('Surajpole Samaaj Trust created.');
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
