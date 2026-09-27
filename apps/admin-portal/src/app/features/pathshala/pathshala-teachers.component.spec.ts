import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { PathshalaTeachersComponent } from './pathshala-teachers.component';
import { PathshalaClass, PathshalaTeacher } from '../../core/admin.models';

const PATHSHALA_ID = 'p1';
const TEACHERS = `/v1/pathshala/pathshalas/${PATHSHALA_ID}/teachers`;

function teacher(overrides: Partial<PathshalaTeacher> = {}): PathshalaTeacher {
  return {
    id: 't1',
    fullName: 'Smt. Kavita Jain',
    phone: '+91 98290 12345',
    email: null,
    qualification: 'Shastri',
    userId: null,
    hasLogin: false,
    classIds: ['c1'],
    addedAt: '2026-09-01T10:00:00Z',
    ...overrides,
  };
}

const CLASSES: PathshalaClass[] = [
  {
    id: 'c1',
    sessionId: 's1',
    sessionLabel: '2026-27',
    name: 'Class 8',
    roomLabel: null,
    schedule: [],
    teacherIds: ['t1'],
    studentCount: 3,
  },
];

describe('PathshalaTeachersComponent', () => {
  let fixture: ComponentFixture<PathshalaTeachersComponent>;
  let component: PathshalaTeachersComponent;
  let http: HttpTestingController;

  const click = { currentTarget: null } as unknown as MouseEvent;
  const text = () => fixture.nativeElement.textContent as string;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PathshalaTeachersComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function signIn(roles: string[]) {
    TestBed.inject(AuthService).ensureCurrentUser().subscribe();
    http.expectOne('/v1/identity/me').flush({
      userId: 'u9',
      tenantId: 't1',
      tenantSlug: 'dasha-nagda',
      mobileOrEmail: 'admin@example.com',
      fullName: 'Admin',
      status: 'Active',
      isContactVerified: true,
      lastLoginAt: null,
      roles,
      permissions: ['Pathshala.Manage'],
      areaLocalityId: null,
    });
  }

  function load(found: PathshalaTeacher[] = [teacher()], canManage = true) {
    fixture = TestBed.createComponent(PathshalaTeachersComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('pathshalaId', PATHSHALA_ID);
    fixture.componentRef.setInput('classes', CLASSES);
    fixture.componentRef.setInput('canManage', canManage);
    fixture.detectChanges();
    http.expectOne(TEACHERS).flush(found);
    fixture.detectChanges();
  }

  it('lists each teacher with what they teach and whether they can sign in', () => {
    signIn(['SamaajAdmin']);
    load([teacher(), teacher({ id: 't2', fullName: 'Shri Rajesh Jain', classIds: [], hasLogin: true, userId: 'u2' })]);

    expect(text()).toContain('Smt. Kavita Jain');
    expect(text()).toContain('Class 8');
    expect(text()).toContain('No class yet');
    expect(text()).toContain('No login');
    expect(text()).toContain('Can sign in');

    const phone = fixture.nativeElement.querySelector('a[href^="tel:"]') as HTMLAnchorElement;

    expect(phone.getAttribute('href')).toBe('tel:+91 98290 12345');
  });

  it('adds a teacher who need not be a member, blanks sent as null', () => {
    signIn(['SamaajAdmin']);
    load([]);

    component.openAdd(click);
    component.fullName = '  Pt. Suresh Shastri ';
    component.phone = '';
    component.email = '';
    component.qualification = 'Acharya';
    component.saveTeacher();

    const call = http.expectOne(TEACHERS);

    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual({
      fullName: 'Pt. Suresh Shastri',
      phone: null,
      email: null,
      qualification: 'Acharya',
      userId: null,
    });

    call.flush(teacher());
    http.expectOne(TEACHERS).flush([teacher()]);
    fixture.detectChanges();

    expect(text()).toContain('Pt. Suresh Shastri added');
    expect(component.editing()).toBeNull();
  });

  it('refuses a phone number the service would refuse', () => {
    signIn(['SamaajAdmin']);
    load([]);

    component.openAdd(click);
    component.fullName = 'Pt. Suresh Shastri';
    component.phone = 'call the office';

    expect(component.canSave()).toBe(false);

    component.saveTeacher();
    http.expectNone(TEACHERS);
  });

  it('keeps the linked login when only the details are edited', () => {
    signIn(['SamaajAdmin']);
    load([teacher({ userId: 'u2', hasLogin: true })]);

    component.openEdit(component.teachers()[0]!, click);
    component.qualification = 'Acharya';
    component.saveTeacher();

    const call = http.expectOne(`${TEACHERS}/t1`);

    expect(call.request.method).toBe('PATCH');
    expect(call.request.body.userId).toBe('u2');

    call.flush(teacher());
    http.expectOne(TEACHERS).flush([teacher()]);
  });

  it('keeps the dialog open, with what was typed, when a save is refused', () => {
    signIn(['SamaajAdmin']);
    load([]);

    component.openAdd(click);
    component.fullName = 'Pt. Suresh Shastri';
    component.saveTeacher();

    http.expectOne(TEACHERS).flush(
      { title: 'That login already teaches here.' },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(component.editing()).toBe('new');
    expect(component.fullName).toBe('Pt. Suresh Shastri');
    expect(fixture.nativeElement.querySelector('[role="dialog"] [role="alert"]')).not.toBeNull();
  });

  it('asks before removing, and tells the page the classes changed', () => {
    signIn(['SamaajAdmin']);
    load();

    let changed = 0;
    component.changed.subscribe(() => changed++);

    component.removing.set(component.teachers()[0]!);
    fixture.detectChanges();

    expect(text()).toContain('will stop teaching every class here');
    http.expectNone(`${TEACHERS}/t1`);

    component.remove(component.teachers()[0]!);

    const call = http.expectOne(`${TEACHERS}/t1`);

    expect(call.request.method).toBe('DELETE');

    call.flush('t1');
    http.expectOne(TEACHERS).flush([]);

    expect(changed).toBe(1);
  });

  it('offers a login only to somebody who may grant the role', () => {
    // An Area Admin runs the Pathshala but cannot grant roles, so the button
    // would be a control that always answers 403.
    signIn(['AreaAdmin']);
    load();

    expect(text()).not.toContain('Give a login');
    expect(text()).toContain('only the Samaaj Admin can give');
  });

  it('draws no controls for a teacher reading the tab', () => {
    signIn(['PathshalaTeacher']);
    load([teacher()], false);

    expect(text()).not.toContain('Add a teacher');
    expect(text()).not.toContain('Edit');
  });

  it('links a member’s login: the role first, then the record', () => {
    signIn(['SamaajAdmin']);
    load();

    component.openLogin(component.teachers()[0]!, click);
    component.memberTerm = 'Kavita';
    component.searchMembers();

    http
      .expectOne((r) => r.url === '/v1/members' && r.params.get('term') === 'Kavita')
      .flush({ items: [{ id: 'm7', fullName: 'Kavita Jain', mobile: '9829012345', email: null }], hasMore: false });
    fixture.detectChanges();

    component.linkMember(component.teachers()[0]!, component.memberResults()![0]!);

    const role = http.expectOne('/v1/identity/admins/m7/roles/PathshalaTeacher');

    expect(role.request.body).toEqual({ granted: true });

    role.flush({ userId: 'm7', role: 'PathshalaTeacher', granted: true, changed: true, roles: [] });

    const link = http.expectOne(`${TEACHERS}/t1`);

    expect(link.request.body.userId).toBe('m7');
    expect(link.request.body.fullName).toBe('Smt. Kavita Jain');

    link.flush(teacher({ userId: 'm7', hasLogin: true }));
    http.expectOne(TEACHERS).flush([teacher({ userId: 'm7', hasLogin: true })]);
    fixture.detectChanges();

    expect(component.loginFor()).toBeNull();
    expect(text()).toContain('signs in as Kavita Jain');
  });

  it('creates a login, shows the activation code once, and forgets it on close', () => {
    signIn(['SamaajAdmin']);
    load();

    component.openLogin(component.teachers()[0]!, click);

    // Prefilled from the record, with the spacing the identity rule refuses taken out.
    expect(component.identifier).toBe('+919829012345');

    component.loginMode.set('new');
    component.createLogin(component.teachers()[0]!);

    const invite = http.expectOne('/v1/identity/admins');

    expect(invite.request.body).toEqual({
      fullName: 'Smt. Kavita Jain',
      mobileOrEmail: '+919829012345',
      roles: ['PathshalaTeacher'],
    });

    invite.flush({
      userId: 'u5',
      fullName: 'Smt. Kavita Jain',
      mobileOrEmail: '+919829012345',
      roles: ['PathshalaTeacher'],
      activationCode: 'K7Q2-M9XA',
      codeExpiresAt: '2026-10-04T00:00:00Z',
    });

    const link = http.expectOne(`${TEACHERS}/t1`);

    expect(link.request.body.userId).toBe('u5');

    link.flush(teacher({ userId: 'u5', hasLogin: true }));
    http.expectOne(TEACHERS).flush([teacher({ userId: 'u5', hasLogin: true })]);
    fixture.detectChanges();

    expect(text()).toContain('K7Q2-M9XA');

    component.closeLogin();
    fixture.detectChanges();

    expect(text()).not.toContain('K7Q2-M9XA');
  });

  it('still shows the code when the account was made but linking it failed', () => {
    // The account exists either way; a code that was never shown is an account
    // nobody can activate.
    signIn(['SamaajAdmin']);
    load();

    component.openLogin(component.teachers()[0]!, click);
    component.loginMode.set('new');
    component.createLogin(component.teachers()[0]!);

    http.expectOne('/v1/identity/admins').flush({
      userId: 'u5',
      fullName: 'Smt. Kavita Jain',
      mobileOrEmail: '+919829012345',
      roles: ['PathshalaTeacher'],
      activationCode: 'K7Q2-M9XA',
      codeExpiresAt: '2026-10-04T00:00:00Z',
    });
    http.expectOne(`${TEACHERS}/t1`).flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(text()).toContain('K7Q2-M9XA');
  });

  it('unlinks a login without touching the account', () => {
    signIn(['SamaajAdmin']);
    load([teacher({ userId: 'u2', hasLogin: true })]);

    component.openLogin(component.teachers()[0]!, click);
    fixture.detectChanges();

    expect(text()).toContain('keeps the Pathshala Teacher role');

    component.unlink(component.teachers()[0]!);

    const call = http.expectOne(`${TEACHERS}/t1`);

    expect(call.request.body.userId).toBeNull();

    call.flush(teacher());
    http.expectOne(TEACHERS).flush([teacher()]);
    http.expectNone((r) => r.url.startsWith('/v1/identity'));
  });
});
