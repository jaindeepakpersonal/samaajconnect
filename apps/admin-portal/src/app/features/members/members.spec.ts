import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { AdminMember } from '../../core/admin.models';
import { MemberDetailComponent } from './member-detail.component';
import { MemberListComponent } from './member-list.component';

function member(overrides: Partial<AdminMember> = {}): AdminMember {
  return {
    id: 'm1',
    fullName: 'Meera Shah',
    photoUrl: null,
    locality: 'Udaipur',
    dateOfBirth: '1990-04-02',
    mobile: '+919812345678',
    email: 'meera@example.com',
    address: '12 Old Road',
    profession: 'Architect',
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

  function load(members: AdminMember[] = [member()]) {
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/v1/members').flush(members);
    fixture.detectChanges();
  }

  it('lists the Samaaj it is scoped to', () => {
    load([member(), member({ id: 'm2', fullName: 'Ravi Shah' })]);

    expect(text()).toContain('Meera Shah');
    expect(text()).toContain('Ravi Shah');
  });

  it('sends the search terms the service actually filters on', () => {
    load();

    component.term = 'Meera';
    component.locality = 'Udaipur';
    component.search();

    const request = http.expectOne((r) => r.url === '/v1/members');

    expect(request.request.params.get('term')).toBe('Meera');
    expect(request.request.params.get('locality')).toBe('Udaipur');
    expect(request.request.params.get('limit')).toBe('100');

    request.flush([]);
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

    request.flush([]);
    fixture.detectChanges();
  });

  it('says nobody matched rather than showing an empty table', () => {
    load([]);

    expect(text()).toContain('Nobody in this Samaaj matches that');
  });

  it('explains why profession cannot be searched', () => {
    // The absence is member-family-service's privacy decision, and a screen
    // that simply left the box out would read as an oversight.
    load();

    expect(text()).toContain('carries a privacy level');
  });

  it('warns when the page is full, because the endpoint caps at a hundred', () => {
    load(Array.from({ length: 100 }, (_, i) => member({ id: 'm' + i })));

    expect(text()).toContain('Showing the first 100');
  });

  it('does not warn when the page is not full', () => {
    load([member()]);

    expect(text()).not.toContain('Showing the first 100');
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

  function load(person: AdminMember = member()) {
    fixture.detectChanges();
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
    load(member({ mobile: null, address: null, dateOfBirth: null }));

    expect(component.form.mobile).toBe('');
    expect(component.form.address).toBe('');
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
