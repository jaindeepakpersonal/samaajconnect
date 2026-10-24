import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { AdminMember } from '../../core/admin.models';
import { MemberDetailComponent } from './member-detail.component';
import { MemberListComponent } from './member-list.component';

function member(overrides: Partial<AdminMember> = {}): AdminMember {
  return {
    id: 'm1',
    fullName: 'Meera Shah',
    photoUrl: null,
    gautraId: null,
    dateOfBirth: '1990-04-02',
    mobile: '+919812345678',
    email: 'meera@example.com',
    permanentAddress: { line: '12 Old Road', cityId: null, localityId: 'loc-udaipur' },
    temporaryAddress: null,
    professionId: null,
    qualificationId: null,
    gender: 'Female',
    ...overrides,
  };
}

describe('MemberListComponent', () => {
  let fixture: ComponentFixture<MemberListComponent>;
  let component: MemberListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MemberListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(MemberListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  /** Answers the Locality, Profession and Gautra lists the screen fetches once. */
  function settleLists(): void {
    http.expectOne((r) => r.url === '/v1/master-data/localities')
      .flush([{ id: 'loc-udaipur', cityId: 'city-1', name: 'Udaipur', isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/lookup-items')
      .flush([{ id: 'prof-ca', category: 'ProfessionType', code: 'CA', label: 'Chartered Accountant', sortOrder: 1, isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/gautras').flush([{ id: 'g-kothari', name: 'Kothari', isActive: true }]);
  }

  function load(members: AdminMember[] = [member()]) {
    fixture.detectChanges();
    settleLists();
    http.expectOne((r) => r.url === '/v1/members').flush({ items: members, hasMore: false });
    fixture.detectChanges();
  }

  it('fixes an Area Admin search to their own locality', async () => {
    // A fresh component, created after /me says who this is.
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
      areaLocalityId: 'loc-udaipur',
    });

    fixture = TestBed.createComponent(MemberListComponent);
    component = fixture.componentInstance;
    load();

    // NgModel applies [disabled] in a microtask.
    await fixture.whenStable();
    fixture.detectChanges();

    const select = fixture.nativeElement.querySelector('#locality') as HTMLSelectElement;
    expect(select.disabled).toBe(true);
    expect(text()).not.toContain('All localities');

    component.term = 'Meera';
    component.search();

    expect(
      http.expectOne((r) => r.url === '/v1/members').request.params.get('localityId'),
    ).toBe('loc-udaipur');
  });

  it('lists the Samaaj it is scoped to', () => {
    load([member(), member({ id: 'm2', fullName: 'Ravi Shah' })]);

    expect(text()).toContain('Meera Shah');
    expect(text()).toContain('Ravi Shah');
  });

  it('sends the search terms the service actually filters on', () => {
    load();

    component.term = 'Meera';
    component.localityId = 'loc-udaipur';
    component.search();

    const request = http.expectOne((r) => r.url === '/v1/members');

    expect(request.request.params.get('term')).toBe('Meera');
    expect(request.request.params.get('localityId')).toBe('loc-udaipur');
    expect(request.request.params.get('skip')).toBe('0');
    expect(request.request.params.get('take')).toBe('20');

    request.flush({ items: [], hasMore: false });
    fixture.detectChanges();
  });

  it('omits an empty box rather than sending a blank filter', () => {
    load();

    component.term = '   ';
    component.search();

    const request = http.expectOne((r) => r.url === '/v1/members');

    // A blank `term` is not the same request as no term: the service treats a
    // present-but-empty parameter as a filter, so this would search for members
    // whose name contains nothing in particular.
    expect(request.request.params.has('term')).toBe(false);

    request.flush({ items: [], hasMore: false });
    fixture.detectChanges();
  });

  it('says nobody matched rather than showing an empty table', () => {
    load([]);

    expect(text()).toContain('Nobody in this Samaaj matches that');
  });

  it('shows each member as a card with Gautra, parents, profession and contact details', () => {
    load([member({ gautraId: 'g-kothari', professionId: 'prof-ca', fatherName: 'Mahesh Shah', motherName: 'Sunita Shah' })]);

    const card = fixture.nativeElement.querySelector('.member-card') as HTMLElement;
    expect(card.textContent).toContain('Meera Shah');
    expect(card.textContent).toContain('(Kothari)');
    expect(card.textContent).toContain('Mahesh Shah');
    expect(card.textContent).toContain('Sunita Shah');
    expect(card.textContent).toContain('Chartered Accountant');
    expect(card.textContent).toContain('Udaipur');
    expect(card.textContent).toContain('+919812345678');
  });

  it('filters by Gautra and Profession as well as name and Locality', () => {
    load();

    component.gautraId = 'g-kothari';
    component.professionId = 'prof-ca';
    component.search();

    const request = http.expectOne((r) => r.url === '/v1/members');
    expect(request.request.params.get('gautraId')).toBe('g-kothari');
    expect(request.request.params.get('professionId')).toBe('prof-ca');
    request.flush({ items: [], hasMore: false });

    component.clear();
    const cleared = http.expectOne((r) => r.url === '/v1/members');
    expect(cleared.request.params.has('gautraId')).toBe(false);
    expect(cleared.request.params.has('professionId')).toBe(false);
    cleared.flush({ items: [], hasMore: false });
  });

  const pagerButtons = () =>
    Array.from(fixture.nativeElement.querySelectorAll('.pager-btn')) as HTMLButtonElement[];

  it('disables Next when the server says there is no further page', () => {
    load([member()]);

    const [previous, next] = pagerButtons();
    expect(previous!.disabled).toBe(true);
    expect(next!.disabled).toBe(true);
  });

  it('asks for the next page by skipping the ones already shown', () => {
    fixture.detectChanges();
    settleLists();
    http
      .expectOne((r) => r.url === '/v1/members')
      .flush({ items: Array.from({ length: 20 }, (_, i) => member({ id: 'm' + i })), hasMore: true });
    fixture.detectChanges();

    const [, next] = pagerButtons();
    expect(next!.disabled).toBe(false);
    next!.click();

    const request = http.expectOne((r) => r.url === '/v1/members');
    expect(request.request.params.get('skip')).toBe('20');
    expect(request.request.params.get('take')).toBe('20');
    request.flush({ items: [member({ id: 'last' })], hasMore: false });
    fixture.detectChanges();

    expect(component.page()).toBe(2);
    expect(pagerButtons()[0]!.disabled).toBe(false);
  });
});

describe('MemberDetailComponent', () => {
  let fixture: ComponentFixture<MemberDetailComponent>;
  let component: MemberDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MemberDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', 'm1']]) } },
        },
      ],
    });

    fixture = TestBed.createComponent(MemberDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  /** Flushes the four master-data lists the screen fetches once on init. */
  function settleMasterData(): void {
    http.expectOne((r) => r.url === '/v1/master-data/gautras').flush([]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'ProfessionType')
      .flush([]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'Qualification')
      .flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush([]);
  }

  function load(person: AdminMember = member()) {
    fixture.detectChanges();
    settleMasterData();
    http.expectOne('/v1/members/m1').flush(person);
    fixture.detectChanges();
  }

  it('fills the form from what the service returned', () => {
    load();

    expect(component.form.fullName).toBe('Meera Shah');
    expect(component.form.mobile).toBe('+919812345678');
    expect(component.form.gender).toBe('Female');
  });

  it('turns nulls into empty strings, never the word undefined', () => {
    // An undefined bound to a `<select>` matches no option at all and renders
    // blank rather than showing its first choice.
    load(member({
      mobile: null,
      permanentAddress: { line: null, cityId: null, localityId: null },
      dateOfBirth: null,
    }));

    expect(component.form.mobile).toBe('');
    expect(component.form.permanentAddress.line).toBe('');
    expect(component.form.dateOfBirth).toBe('');
  });

  it('corrects details through the endpoint that carries no privacy fields', () => {
    load();

    component.form.fullName = 'Meera Shaha';
    component.save();

    const request = http.expectOne('/v1/members/m1/details');

    expect(request.request.method).toBe('PATCH');
    expect(request.request.body.fullName).toBe('Meera Shaha');

    // The guarantee this whole screen exists for. Sending either of these would
    // mean an administrator deciding something the member decided - and the
    // service would take it.
    expect(request.request.body).not.toHaveProperty('privacy');
    expect(request.request.body).not.toHaveProperty('isListedInDirectory');

    request.flush(member({ fullName: 'Meera Shaha' }));
    fixture.detectChanges();

    expect(text()).toContain('Saved');
  });

  it('sends a cleared box as null rather than an empty string', () => {
    load();

    component.form.mobile = '  ';
    component.save();

    const request = http.expectOne('/v1/members/m1/details');

    // Removing a wrong number is a real correction, and null is how this
    // service spells "no value".
    expect(request.request.body.mobile).toBeNull();

    request.flush(member({ mobile: null }));
    fixture.detectChanges();
  });

  it('refuses to send a nameless member and says why', () => {
    load();

    component.form.fullName = '   ';
    component.save();

    http.expectNone('/v1/members/m1/details');

    fixture.detectChanges();

    expect(text()).toContain('A member has to have a name');
  });

  it('tells the administrator what this screen cannot change', () => {
    load();

    expect(text()).toContain('cannot be changed here');
  });

  it('shows a refusal rather than pretending it saved', () => {
    load();

    component.save();

    http.expectOne('/v1/members/m1/details').flush(
      { title: 'Conflict', detail: 'This is your own profile.' },
      { status: 409, statusText: 'Conflict' },
    );

    fixture.detectChanges();

    expect(text()).toContain('This is your own profile');
    expect(text()).not.toContain('Saved.');
  });
});
