import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { Trust } from './trust.models';
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

function providers() {
  return [
    provideRouter([]),
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
  ];
}

describe('TrustsListComponent', () => {
  let fixture: ComponentFixture<TrustsListComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [TrustsListComponent], providers: providers() });

    fixture = TestBed.createComponent(TrustsListComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  it('reads a 404 as the module not being switched on', () => {
    fixture.detectChanges();
    http.expectOne('/v1/trust/trusts').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('not switched on');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('says when our Samaaj has not set one up yet', () => {
    fixture.detectChanges();
    http.expectOne('/v1/trust/trusts').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('has not set up a Trust yet');
  });

  it('lists a Trust with its status', () => {
    fixture.detectChanges();
    http.expectOne('/v1/trust/trusts').flush([trust()]);
    http.expectOne('/v1/trust/collections/mine').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('Hiran Magri Samaaj Trust');
    expect(text()).toContain('Active');
    expect(text()).not.toContain('What you owe');
  });

  it('says what the member owes, to which Trust, and for what', () => {
    fixture.detectChanges();
    http.expectOne('/v1/trust/trusts').flush([trust()]);
    http.expectOne('/v1/trust/collections/mine').flush([
      {
        id: 'c1',
        trustId: trust().id,
        sourceType: 'BoliResult',
        sourceId: 'b1',
        amount: 2_100_000,
        createdAt: '2026-09-20T10:00:00Z',
      },
    ]);
    fixture.detectChanges();

    expect(text()).toContain('What you owe');
    expect(text()).toContain('₹21,000 to Hiran Magri Samaaj Trust for a Boli you won');
  });

  it('shows the Trusts even when what is owed cannot be read', () => {
    fixture.detectChanges();
    http.expectOne('/v1/trust/trusts').flush([trust()]);
    http.expectOne('/v1/trust/collections/mine').flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(text()).toContain('Hiran Magri Samaaj Trust');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
