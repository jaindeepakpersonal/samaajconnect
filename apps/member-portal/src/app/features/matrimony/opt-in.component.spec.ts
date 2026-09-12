import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { MyProfile } from '../members/members.models';
import { MatrimonyOptInComponent } from './opt-in.component';
import { MatrimonyConsentNotice, MyBiodata } from './matrimony.models';

function profile(overrides: Partial<MyProfile> = {}): MyProfile {
  return {
    id: 'm1',
    tenantId: 't1',
    fullName: 'Ravi Shah',
    photoUrl: null,
    dateOfBirth: '1990-05-14',
    gender: 'Male',
    mobile: '+919812345678',
    email: 'ravi@example.com',
    gautraId: 'gautra-1',
    professionId: null,
    qualificationId: null,
    permanentAddress: { line: null, cityId: 'city-1', localityId: 'loc-1' },
    temporaryAddress: null,
    privacy: {
      mobile: 'SamaajOnly',
      email: 'Private',
      address: 'Private',
      profession: 'SamaajOnly',
      dateOfBirth: 'Private',
      qualification: 'SamaajOnly',
    },
    isListedInDirectory: true,
    createdAt: '2026-01-01T10:00:00Z',
    updatedAt: null,
    ...overrides,
  };
}

function notice(): MatrimonyConsentNotice {
  return {
    version: '2026-09-12.1',
    summary: 'We show your biodata to other members of your own Samaaj.',
    attestation: 'I consent to my biodata being shown to other members of my own Samaaj.',
  };
}

function myBiodata(overrides: Partial<MyBiodata> = {}): MyBiodata {
  return {
    biodata: {
      memberId: 'm1',
      fullName: 'Ravi Shah',
      photoUrl: null,
      gender: 'Male',
      age: 30,
      gautraId: null,
      professionId: null,
      qualificationId: null,
      cityId: null,
      localityId: null,
      heightCm: null,
      maritalStatusId: null,
      diet: null,
      familyType: null,
      fatherOccupation: null,
      motherOccupation: null,
      siblingsCount: null,
      manglik: null,
      horoscopeNotes: null,
      aboutMe: null,
      preferredAgeMin: null,
      preferredAgeMax: null,
      partnerPreferenceNotes: null,
    },
    dateOfBirth: '1995-04-02',
    isActive: true,
    consentGivenAt: '2026-09-12T00:00:00Z',
    consentNoticeVersion: '2026-09-12.1',
    createdAt: '2026-09-12T00:00:00Z',
    updatedAt: null,
    ...overrides,
  };
}

describe('MatrimonyOptInComponent', () => {
  let fixture: ComponentFixture<MatrimonyOptInComponent>;
  let component: MatrimonyOptInComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MatrimonyOptInComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(MatrimonyOptInComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  /** The five master-data lists fetched once on init. */
  function settleMasterData(): void {
    http.expectOne((r) => r.url === '/v1/master-data/gautras').flush([]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'ProfessionType')
      .flush([]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'Qualification')
      .flush([]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'MaritalStatus')
      .flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush([]);
  }

  it('shows the consent notice and prefills from the base profile when never opted in', () => {
    fixture.detectChanges();
    settleMasterData();

    http.expectOne('/v1/matrimony/biodata/me').flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne('/v1/matrimony/consent-notice').flush(notice());
    http.expectOne('/v1/members/me').flush(profile());
    http.expectOne((r) => r.url === '/v1/master-data/localities' && r.params.get('cityId') === 'city-1').flush([]);

    fixture.detectChanges();

    expect(component.form.fullName).toBe('Ravi Shah');
    expect(component.form.gautraId).toBe('gautra-1');
    expect(component.form.cityId).toBe('city-1');
    expect(text()).toContain('We show your biodata');
  });

  /**
   * A base profile with no date of birth set - the ordinary case for a
   * fresh registration - used to leave the opt-in form's date field empty
   * and let the member submit anyway, sending an empty string the service
   * could only answer with an unhandled 500. The submit button must be
   * disabled instead, the same way a native required field blocks a plain
   * HTML form - Angular's own NgForm validity, since this app suppresses
   * native constraint validation.
   */
  it('disables Opt in until a date of birth is filled in', async () => {
    fixture.detectChanges();
    settleMasterData();

    http.expectOne('/v1/matrimony/biodata/me').flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne('/v1/matrimony/consent-notice').flush(notice());
    http.expectOne('/v1/members/me').flush(profile({ dateOfBirth: null }));
    http.expectOne((r) => r.url === '/v1/master-data/localities' && r.params.get('cityId') === 'city-1').flush([]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const submit = (fixture.nativeElement as HTMLElement).querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;

    expect(submit.disabled).toBe(true);
  });

  it('refuses to submit without ticking the consent box', () => {
    fixture.detectChanges();
    settleMasterData();

    http.expectOne('/v1/matrimony/biodata/me').flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne('/v1/matrimony/consent-notice').flush(notice());
    http.expectOne('/v1/members/me').flush(profile());
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);

    fixture.detectChanges();

    component.consentGiven = false;
    component.save();

    const request = http.expectOne('/v1/matrimony/biodata/me/opt-in');

    expect(request.request.body.consentGiven).toBe(false);

    request.flush(
      { title: 'Validation failed', errors: { consentGiven: ['Consent is required.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(component.saveError()).not.toBeNull();
  });

  it('shows the edit form, not the notice, once already opted in', () => {
    fixture.detectChanges();
    settleMasterData();

    http.expectOne('/v1/matrimony/biodata/me').flush(myBiodata());
    fixture.detectChanges();

    expect(text()).toContain('Edit your biodata');
    expect(text()).toContain('Visible in search');
    expect(text()).not.toContain('We show your biodata');
  });

  it('withdraws and re-reads the profile as no longer active', () => {
    fixture.detectChanges();
    settleMasterData();

    http.expectOne('/v1/matrimony/biodata/me').flush(myBiodata());
    fixture.detectChanges();

    component.confirmingWithdraw.set(true);
    component.withdraw();

    http.expectOne('/v1/matrimony/biodata/me/consent').flush({ withdrawn: true });
    http.expectOne('/v1/matrimony/biodata/me').flush(myBiodata({ isActive: false }));
    fixture.detectChanges();

    expect(text()).toContain('Withdrawn - not visible in search');
  });
});
