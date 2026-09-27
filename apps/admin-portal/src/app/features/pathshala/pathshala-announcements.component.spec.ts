import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_CONFIG } from '@samaajconnect/shared';
import { PathshalaAnnouncementsComponent } from './pathshala-announcements.component';
import { PathshalaAnnouncement } from '../../core/admin.models';

const ANNOUNCEMENTS = '/v1/pathshala/pathshalas/p1/announcements';

function sent(overrides: Partial<PathshalaAnnouncement> = {}): PathshalaAnnouncement {
  return {
    id: 'a1',
    pathshalaId: 'p1',
    pathshalaName: 'Shri Mahavir Jain Pathshala',
    title: 'No class this Sunday',
    body: 'The hall is booked for Paryushan.',
    sentBy: 'u1',
    sentAt: '2026-09-20T10:00:00Z',
    recipientCount: 14,
    ...overrides,
  };
}

describe('PathshalaAnnouncementsComponent', () => {
  let fixture: ComponentFixture<PathshalaAnnouncementsComponent>;
  let component: PathshalaAnnouncementsComponent;
  let http: HttpTestingController;

  const text = () => fixture.nativeElement.textContent as string;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PathshalaAnnouncementsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PathshalaAnnouncementsComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('pathshalaId', 'p1');
  });

  afterEach(() => http.verify());

  function load(found: PathshalaAnnouncement[] = []) {
    fixture.detectChanges();
    http.expectOne(ANNOUNCEMENTS).flush(found);
    fixture.detectChanges();
  }

  it('lists what has been sent, and to how many', () => {
    load([sent()]);

    expect(text()).toContain('No class this Sunday');
    expect(text()).toContain('14 people');
  });

  it('sends the message to the parents and clears the form', () => {
    load();

    component.title = ' No class this Sunday ';
    component.body = ' The hall is booked. ';
    component.send();

    const call = http.expectOne(ANNOUNCEMENTS);

    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual({ title: 'No class this Sunday', body: 'The hall is booked.' });

    call.flush(sent());
    http.expectOne(ANNOUNCEMENTS).flush([sent()]);
    fixture.detectChanges();

    expect(text()).toContain('Sent to 14 people');
    expect(component.title).toBe('');
  });

  it('keeps what was typed when the service refuses it', () => {
    // A Pathshala with no children yet is refused rather than sent to nobody.
    load();

    component.title = 'Welcome';
    component.body = 'Classes start on Sunday.';
    component.send();

    http
      .expectOne(ANNOUNCEMENTS)
      .flush({ title: 'There is nobody to send it to.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(component.title).toBe('Welcome');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('will not send without a title and a message', () => {
    load();

    component.title = 'Welcome';
    component.body = '   ';
    component.send();

    http.expectNone((r) => r.method === 'POST');
  });
});
