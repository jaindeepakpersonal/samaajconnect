import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { FamilyOverview } from '../../core/admin.models';
import { LocalityOptions } from '../../core/locality-options';
import { FamilyListComponent } from './family-list.component';

function family(overrides: Partial<FamilyOverview> = {}): FamilyOverview {
  return {
    id: 'f1',
    familyCode: 'SHAH42',
    headMemberId: 'm1',
    createdAt: '2026-09-01T00:00:00Z',
    members: [
      { memberId: 'm1', fullName: 'Ravi Shah', relationship: 'Spouse', isHead: true, localityId: 'loc1' },
      { memberId: 'm2', fullName: 'Meera Shah', relationship: 'Spouse', isHead: false, localityId: null },
    ],
    children: [{ id: 'c1', fullName: 'Diya Shah', age: 10, gender: 'Female' }],
    ...overrides,
  };
}

describe('FamilyListComponent', () => {
  let fixture: ComponentFixture<FamilyListComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FamilyListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: LocalityOptions,
          useValue: {
            load: () => undefined,
            options: () => [
              { id: 'loc1', label: 'Hiran Magri, Udaipur' },
              { id: 'loc9', label: 'Surajpole, Udaipur' },
            ],
            label: (id: string | null) => (id === 'loc1' ? 'Hiran Magri, Udaipur' : 'Whole Samaaj'),
          },
        },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function create() {
    fixture = TestBed.createComponent(FamilyListComponent);
    fixture.detectChanges();
  }

  it('lists households with members, where they live, and children as ages', () => {
    create();
    http.expectOne('/v1/families?skip=0&take=20').flush({ items: [family()], hasMore: false });
    fixture.detectChanges();

    expect(text()).toContain('SHAH42');
    expect(text()).toContain('Ravi Shah');
    expect(text()).toContain('Head');
    expect(text()).toContain('Hiran Magri, Udaipur');
    expect(text()).toContain('No locality');
    expect(text()).toContain('Diya Shah');
    expect(text()).toContain('· 10 ·');
    expect(text()).toContain('adult child conversion queue');

    const options = [...(fixture.nativeElement as HTMLElement).querySelectorAll('option')].map((o) =>
      o.textContent?.trim(),
    );
    expect(options).not.toContain('The whole Samaaj only');
  });

  it('asks the service for one locality, from the first page', () => {
    create();
    http.expectOne('/v1/families?skip=0&take=20').flush({ items: [family()], hasMore: true });
    fixture.detectChanges();

    fixture.componentInstance.goTo(2);
    http.expectOne('/v1/families?skip=20&take=20').flush({ items: [], hasMore: false });

    fixture.componentInstance.setLocality('loc9');
    http.expectOne('/v1/families?skip=0&take=20&localityId=loc9').flush({ items: [], hasMore: false });
    fixture.detectChanges();

    expect(fixture.componentInstance.page()).toBe(1);
    expect(text()).toContain('No household has a member living in that locality');
  });

  it('fixes an Area Admin to their own locality, with no conversion queue link', async () => {
    const auth = TestBed.inject(AuthService);
    auth.ensureCurrentUser().subscribe();
    http.expectOne('/v1/identity/me').flush({
      userId: 'u9',
      tenantId: 't1',
      tenantSlug: 'dasha-nagda',
      mobileOrEmail: 'area@example.com',
      fullName: 'Area Admin',
      status: 'Active',
      isContactVerified: true,
      lastLoginAt: null,
      roles: ['Member', 'AreaAdmin'],
      permissions: ['Members.Read', 'Members.Write'],
      areaLocalityId: 'loc1',
    });

    create();
    http.expectOne('/v1/families?skip=0&take=20&localityId=loc1').flush({ items: [], hasMore: false });
    await fixture.whenStable();
    fixture.detectChanges();

    const select = fixture.nativeElement.querySelector('#families-locality') as HTMLSelectElement;
    expect(select.disabled).toBe(true);
    expect(text()).not.toContain('All localities');
    expect(text()).not.toContain('conversion queue');
  });
});
