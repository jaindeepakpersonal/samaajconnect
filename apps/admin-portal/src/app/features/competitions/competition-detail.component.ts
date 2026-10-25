import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import {
  City,
  Competition,
  CompetitionDetail,
  CompetitionFinalResult,
  CompetitionResult,
  CompetitionStatus,
  JudgePanel,
  LookupItem,
  Round,
  RoundDetail,
  Scoresheet,
  ScoresheetEntry,
} from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';
import { LocalityOptions } from '../../core/locality-options';
import { CompetitionDialogComponent } from './competition-dialog.component';

/**
 * One competition, run from six tabs with every form in a popup (2026-10-25).
 *
 * No wireframe covers this. **Details** is what was set up - the type, the
 * window, the rounds, max marks and the audition - with the cover photo.
 * **Participants** is who applied: members apply from the member portal, and
 * nobody enters them by hand. **Judges** sit on a panel - Main, Audition or
 * Both when there is an audition. **Rounds** come from the format when judging
 * starts, with everybody entered: the audition first if there is one, whose
 * closing puts the top N into every main round. **Scores** is the organiser's
 * panel - every judge's mark on every participant, and the standings they add
 * up to. **Results** publishes the winners once every round is closed.
 *
 * Recording a mark is not here: it is a judge's own act, on the member portal,
 * because a judge is a member the organiser named rather than an administrator.
 */
@Component({
  selector: 'app-competition-detail',
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, AuthedImageDirective, CompetitionDialogComponent],
  template: `
    <p><a class="btn link" routerLink="/competitions">← All competitions</a></p>

    @if (error(); as message) {
      @if (!popup()) {
        <p class="notice error" role="alert">{{ message }}</p>
      }
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such competition in {{ scope.label() }}.</p>
    } @else if (competition(); as subject) {
      <!-- The head: what it is, where, and the one stage it can move to ------ -->
      <div class="card">
        @if (subject.photoUrl; as path) {
          <img class="competition-cover" [scAuthedSrc]="path" alt="" />
        }

        <div class="section-head">
          <div>
            <h1 class="title">{{ subject.title }}</h1>
            <p class="sub">
              {{ stageLabel(subject.status) }} · {{ subject.registrationCount }} applied
              @if (typeName(subject); as type) { · {{ type }} }
            </p>
            @if (whereHeld(subject); as where) {
              <p class="small">{{ where }}</p>
            }
          </div>

          <div class="row-actions">
            @if (editable()) {
              <button class="btn alt" type="button" (click)="openPopup('edit')">Edit</button>
            }

            @if (nextStatus(); as next) {
              <button class="btn" type="button" [disabled]="busy()" (click)="askToMove(next)">
                {{ moveLabel(next) }}
              </button>
            } @else {
              <span class="pill off">Closed</span>
            }
          </div>
        </div>
      </div>

      <div class="tabs" role="tablist" aria-label="This competition">
        @for (item of tabs; track item.value) {
          <button
            type="button"
            role="tab"
            [id]="'competition-tab-' + item.value"
            [class.on]="tab() === item.value"
            [attr.aria-selected]="tab() === item.value"
            aria-controls="competition-tab-panel"
            (click)="showTab(item.value)"
          >
            {{ item.label }}{{ countFor(item.value) }}
          </button>
        }
      </div>

      <div id="competition-tab-panel" role="tabpanel" [attr.aria-labelledby]="'competition-tab-' + tab()">
        @switch (tab()) {
          <!-- Participants ------------------------------------------------- -->
          @case ('participants') {
            <div class="card">
              <h2>Participants</h2>
              <p class="small">
                Members apply from the member portal while registration is open. When judging
                starts, everybody here is entered -
                {{ subject.auditionRequired ? 'in the audition, and the finalists in every round.' : 'in every round.' }}
              </p>

              @if (registered().length === 0) {
                <p class="empty">Nobody has applied yet.</p>
              } @else {
                <div class="table-wrap">
                  <table>
                    <caption class="sr-only">Members who applied, in the order they applied</caption>
                    <thead><tr><th>#</th><th>Member</th></tr></thead>
                    <tbody>
                      @for (memberId of registered(); track memberId; let i = $index) {
                        <tr>
                          <td>{{ i + 1 }}</td>
                          <td><b><a [routerLink]="['/members', memberId]">{{ memberName(memberId) }}</a></b></td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>
          }

          <!-- Judges ------------------------------------------------------- -->
          @case ('judges') {
            <div class="card">
              <div class="section-head">
                <h2>Judges</h2>
                <button class="btn small" type="button" (click)="openJudge(null)">Assign a judge</button>
              </div>

              <p class="small">
                A judge marks the participants on their own sheet in the member portal.
                @if (subject.auditionRequired) {
                  The audition panel marks the audition, the main panel every round; a judge may sit on both.
                }
              </p>

              @if (judges().length === 0) {
                <p class="empty">Nobody assigned yet.</p>
              } @else {
                <div class="table-wrap">
                  <table>
                    <caption class="sr-only">Assigned judges and their panels</caption>
                    <thead><tr><th>Member</th><th>Panel</th><th>Assigned</th><th></th></tr></thead>
                    <tbody>
                      @for (judge of judges(); track judge.id) {
                        <tr>
                          <td><b><a [routerLink]="['/members', judge.memberId]">{{ memberName(judge.memberId) }}</a></b></td>
                          <td>{{ panelLabel(judge.panel) }}</td>
                          <td>{{ judge.assignedAt | date: 'd MMM y' }}</td>
                          <td>
                            <div class="row-actions">
                              @if (subject.auditionRequired) {
                                <button class="btn small alt" type="button" (click)="openJudge(judge.memberId)">
                                  Change panel<span class="sr-only"> for {{ memberName(judge.memberId) }}</span>
                                </button>
                              }
                              <button class="btn small alt" type="button" [disabled]="busy()"
                                (click)="removeJudge(judge.memberId)">
                                Remove<span class="sr-only"> {{ memberName(judge.memberId) }}</span>
                              </button>
                            </div>
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>
          }

          <!-- Rounds ------------------------------------------------------- -->
          @case ('rounds') {
            <div class="card">
              <h2>Rounds</h2>

              @if (rounds().length === 0) {
                <p class="empty">
                  The rounds are made when judging starts, with everybody who applied entered:
                  {{ plannedRounds(subject) }}.
                </p>
              } @else {
                <div class="table-wrap">
                  <table>
                    <caption class="sr-only">This competition's rounds</caption>
                    <thead>
                      <tr><th>Round</th><th>Participants</th><th>Status</th><th></th></tr>
                    </thead>
                    <tbody>
                      @for (round of rounds(); track round.id) {
                        <tr>
                          <td>
                            <b>{{ round.name }}</b>
                            @if (round.kind === 'Audition') {
                              <div class="muted">Top {{ subject.finalistCount }} go through</div>
                            }
                          </td>
                          <td>{{ round.entryCount }}</td>
                          <td>
                            <span class="pill" [class.warn]="round.status === 'Open'">{{ round.status }}</span>
                            @if (round.hasPublishedResult) {
                              <div class="muted">Result published</div>
                            }
                          </td>
                          <td>
                            <div class="row-actions">
                              <button class="btn small alt" type="button" (click)="showRound(round)">
                                View<span class="sr-only"> {{ round.name }}</span>
                              </button>

                              @if (round.status === 'Open') {
                                <button class="btn small" type="button" [disabled]="busy()" (click)="askToClose(round)">
                                  {{ round.kind === 'Audition' ? 'Close audition' : 'Close' }}<span class="sr-only"> {{ round.name }}</span>
                                </button>
                              } @else if (round.kind === 'Main' && subject.roundCount > 0 && !round.hasPublishedResult) {
                                <button class="btn small alt" type="button" [disabled]="busy()" (click)="publish(round.id)">
                                  Publish round result<span class="sr-only"> for {{ round.name }}</span>
                                </button>
                              }
                            </div>
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>
          }

          <!-- Scores: the organiser's panel --------------------------------- -->
          @case ('scores') {
            @if (sheetLoading()) {
              <p class="empty" role="status">Loading…</p>
            } @else if (sheet(); as scores) {
              @if (scores.rounds.length === 0) {
                <div class="card"><p class="empty">Nothing to score until judging starts.</p></div>
              } @else {
                <div class="card">
                  <h2>Standings now</h2>
                  <p class="small">
                    Each participant's round averages added up, out of {{ scores.maxMarks }} a round.
                    Live until the result is published. Audition marks only decide who takes part.
                  </p>
                  @if (scores.standings.length === 0) {
                    <p class="empty">Nobody is in a round yet.</p>
                  } @else {
                    <div class="table-wrap">
                      <table>
                        <caption class="sr-only">Standings from every judge's marks</caption>
                        <thead><tr><th>Place</th><th>Participant</th><th>Total</th></tr></thead>
                        <tbody>
                          @for (standing of scores.standings; track standing.memberId) {
                            <tr [class.podium]="standing.rank <= 3">
                              <td>{{ standing.rank }}</td>
                              <td><b><a [routerLink]="['/members', standing.memberId]">{{ memberName(standing.memberId) }}</a></b></td>
                              <td>{{ standing.total | number: '1.0-2' }}</td>
                            </tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  }
                </div>

                @for (sheetRound of scores.rounds; track sheetRound.round.id) {
                  <div class="card spaced">
                    <h2>{{ sheetRound.round.name }}</h2>
                    @if (sheetRound.entries.length === 0) {
                      <p class="empty">
                        {{ sheetRound.round.kind === 'Main' && subject.auditionRequired ? 'The finalists are entered when the audition closes.' : 'Nobody in this round.' }}
                      </p>
                    } @else {
                      <div class="table-wrap">
                        <table>
                          <caption class="sr-only">Every judge's marks in {{ sheetRound.round.name }}</caption>
                          <thead>
                            <tr>
                              <th>Participant</th>
                              @for (judge of judgesFor(sheetRound.round, scores); track judge.memberId) {
                                <th>{{ memberName(judge.memberId) }}</th>
                              }
                              <th>Average</th>
                              @if (sheetRound.round.kind === 'Audition') { <th>Through</th> }
                            </tr>
                          </thead>
                          <tbody>
                            @for (entry of sheetRound.entries; track entry.entryId) {
                              <tr>
                                <td><b><a [routerLink]="['/members', entry.participantMemberId]">{{ memberName(entry.participantMemberId) }}</a></b></td>
                                @for (judge of judgesFor(sheetRound.round, scores); track judge.memberId) {
                                  <td [title]="markOf(entry, judge.memberId)?.comments ?? ''">
                                    {{ markOf(entry, judge.memberId)?.value ?? '—' }}
                                  </td>
                                }
                                <td><b>{{ entry.average === null ? '—' : (entry.average | number: '1.0-2') }}</b></td>
                                @if (sheetRound.round.kind === 'Audition') {
                                  <td>
                                    @if (entry.wentThrough === true) {
                                      <span class="pill ok">Through</span>
                                    } @else if (entry.wentThrough === false) {
                                      <span class="pill off">Not through</span>
                                    } @else {
                                      <span class="muted">Not decided</span>
                                    }
                                  </td>
                                }
                              </tr>
                            }
                          </tbody>
                        </table>
                      </div>
                    }
                  </div>
                }
              }
            }
          }

          <!-- Results ------------------------------------------------------ -->
          @case ('results') {
            <div class="card">
              <h2>Results</h2>

              @if (finalResult(); as final) {
                <p class="small">Published {{ final.publishedAt | date: 'd MMM y, h:mm a' }}. Frozen as it was announced.</p>

                <ol class="podium-list">
                  @for (winner of final.winners; track winner.memberId) {
                    <li>
                      <span class="pill" [class.ok]="winner.rank === 1">{{ placeName(winner.rank) }}</span>
                      <b><a [routerLink]="['/members', winner.memberId]">{{ memberName(winner.memberId) }}</a></b>
                      <span class="muted">{{ winner.total | number: '1.0-2' }}</span>
                    </li>
                  }
                </ol>

                @if (final.standings.length > final.winners.length) {
                  <h3>Every place</h3>
                  <div class="table-wrap">
                    <table>
                      <caption class="sr-only">The final standings, every place</caption>
                      <thead><tr><th>Place</th><th>Participant</th><th>Total</th></tr></thead>
                      <tbody>
                        @for (standing of final.standings; track standing.memberId) {
                          <tr>
                            <td>{{ standing.rank }}</td>
                            <td>{{ memberName(standing.memberId) }}</td>
                            <td>{{ standing.total | number: '1.0-2' }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }
              } @else if (rounds().length === 0) {
                <p class="empty">Nothing to publish until judging starts.</p>
              } @else if (roundsStillOpen() > 0) {
                <p class="empty">
                  {{ roundsStillOpen() }} round(s) still open. Close every round, and the winners can be published.
                </p>
              } @else {
                <p>Every round is closed. Publishing names the first, second and third places to every member.</p>
                <button class="btn" type="button" [disabled]="busy()" (click)="openPopup('publish')">Publish the result</button>
              }
            </div>
          }

          <!-- Details ------------------------------------------------------ -->
          @default {
            <div class="card">
              <h2>Details</h2>

              @if (subject.description) {
                <p>{{ subject.description }}</p>
              }

              <dl class="details">
                <dt>Type</dt><dd>{{ typeName(subject) ?? 'Not specified' }}</dd>
                <dt>Registration</dt>
                <dd>{{ subject.registrationStartAt | date: 'd MMM y, h:mm a' }} — {{ subject.registrationEndAt | date: 'd MMM y, h:mm a' }}</dd>
                <dt>Rounds</dt>
                <dd>{{ subject.roundCount === 0 ? 'None - one judging, and the top three are shown' : subject.roundCount }}</dd>
                <dt>Max marks</dt><dd>{{ subject.maxMarks }} per judge, per round</dd>
                <dt>Audition</dt>
                <dd>
                  @if (subject.auditionRequired) {
                    {{ subject.auditionDate | date: 'd MMM y' }} - the top {{ subject.finalistCount }} go through
                  } @else {
                    None - everybody who applies takes part in every round
                  }
                </dd>
              </dl>
            </div>

            <div class="card spaced">
              <h2>Photo</h2>
              <p class="small">
                The cover across the top of the competition's page, in both portals. It can change at
                any stage. JPEG, PNG or WebP, up to 2 MB; a wide picture suits a cover best.
              </p>
              <label for="competition-photo">{{ subject.photoUrl ? 'Change the photo' : 'Add a photo' }}</label>
              <input id="competition-photo" class="input" type="file" accept="image/jpeg,image/png,image/webp"
                [disabled]="busy()" (change)="uploadPhoto($event)" />
              @if (subject.photoUrl) {
                <div class="actions">
                  <button class="btn small alt" type="button" [disabled]="busy()" (click)="removePhoto()">
                    Remove the photo
                  </button>
                </div>
              }
            </div>
          }
        }
      </div>

      <!-- Popups ---------------------------------------------------------- -->
      @if (popup() === 'edit') {
        <app-competition-dialog [competition]="subject" (saved)="onSaved($event)" (closed)="closePopup()" />
      }

      @if (popup() === 'judge') {
        <div class="modal-backdrop" (click)="closePopup()">
          <div class="card modal-card modal-dialog" role="dialog" aria-modal="true" aria-labelledby="judge-popup-title"
            (click)="$event.stopPropagation()" (keydown.escape)="closePopup()">
            <h2 id="judge-popup-title">{{ judgeEditing() ? 'Change ' + memberName(judgeEditing()!) + '\\'s panel' : 'Assign a judge' }}</h2>

            @if (error(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <form (ngSubmit)="assignJudge()">
              @if (!judgeEditing()) {
                <label for="judge-picker">Member</label>
                <select id="judge-picker" class="input" name="judgeMemberId" [(ngModel)]="judgeMemberId">
                  <option value="">Choose a member…</option>
                  @for (member of judgeCandidates(); track member.id) {
                    <option [value]="member.id">{{ member.fullName }}</option>
                  }
                </select>
              }

              @if (subject.auditionRequired) {
                <label for="judge-panel">Judges</label>
                <select id="judge-panel" class="input" name="judgePanel" [(ngModel)]="judgePanel">
                  <option value="Main">The main rounds</option>
                  <option value="Audition">The audition</option>
                  <option value="Both">Both</option>
                </select>
              }

              <div class="actions">
                <button class="btn" type="submit" [disabled]="busy() || !judgeMemberId">
                  {{ judgeEditing() ? 'Save' : 'Assign as judge' }}
                </button>
                <button class="btn alt" type="button" (click)="closePopup()">Cancel</button>
              </div>
            </form>
          </div>
        </div>
      }

      @if (popup() === 'round') {
        @if (selectedRound(); as round) {
          <div class="modal-backdrop" (click)="closePopup()">
            <div class="card modal-card modal-dialog wide" role="dialog" aria-modal="true" aria-labelledby="round-popup-title"
              (click)="$event.stopPropagation()" (keydown.escape)="closePopup()">
              <h2 id="round-popup-title">{{ round.name }}</h2>

              @if (error(); as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              @if (roundLoading()) {
                <p class="empty" role="status">Loading…</p>
              } @else if (roundDetail(); as rd) {
                @if (rd.entries.length === 0) {
                  <p class="empty">Nobody in this round.</p>
                } @else {
                  <div class="table-wrap">
                    <table>
                      <caption class="sr-only">Participants in {{ round.name }}</caption>
                      <thead><tr><th>Participant</th><th>Average</th></tr></thead>
                      <tbody>
                        @for (entry of rd.entries; track entry.id) {
                          <tr>
                            <td><b><a [routerLink]="['/members', entry.participantMemberId]">{{ memberName(entry.participantMemberId) }}</a></b></td>
                            <td>{{ entry.averageScore === null ? '—' : (entry.averageScore | number: '1.0-2') }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }

                @if (round.status === 'Open' && unenteredRegistrants().length > 0) {
                  <form class="inline-form spaced" (ngSubmit)="createEntry(round.id)">
                    <label class="sr-only" for="entry-picker">Add somebody who applied</label>
                    <select id="entry-picker" class="input" name="entryMemberId" [(ngModel)]="entryMemberId">
                      <option value="">Add somebody who applied…</option>
                      @for (memberId of unenteredRegistrants(); track memberId) {
                        <option [value]="memberId">{{ memberName(memberId) }}</option>
                      }
                    </select>
                    <button class="btn small" type="submit" [disabled]="busy() || !entryMemberId">Add to this round</button>
                  </form>
                }
              }

              @if (roundResult(); as ranking) {
                <h3>Published result</h3>
                <ol class="podium-list">
                  @for (entry of ranking.ranking; track entry.entryId) {
                    <li>
                      <span class="muted">{{ entry.rank }}.</span>
                      <b>{{ memberName(entry.participantMemberId) }}</b>
                      <span class="muted">{{ entry.averageScore | number: '1.0-2' }}</span>
                    </li>
                  }
                </ol>
              }

              <div class="actions">
                <button class="btn alt" type="button" (click)="closePopup()">Close</button>
              </div>
            </div>
          </div>
        }
      }

      @if (popup() === 'confirm') {
        <div class="modal-backdrop" (click)="closePopup()">
          <div class="card modal-card modal-dialog" role="dialog" aria-modal="true" aria-labelledby="confirm-popup-title"
            (click)="$event.stopPropagation()" (keydown.escape)="closePopup()">
            <h2 id="confirm-popup-title">{{ confirmTitle() }}</h2>

            @if (error(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <p class="notice warn" role="status">{{ confirmText() }}</p>

            <div class="actions">
              <button class="btn" type="button" [disabled]="busy()" (click)="confirm()">{{ confirmTitle() }}</button>
              <button class="btn alt" type="button" (click)="closePopup()">Cancel</button>
            </div>
          </div>
        </div>
      }

      @if (popup() === 'publish') {
        <div class="modal-backdrop" (click)="closePopup()">
          <div class="card modal-card modal-dialog" role="dialog" aria-modal="true" aria-labelledby="publish-popup-title"
            (click)="$event.stopPropagation()" (keydown.escape)="closePopup()">
            <h2 id="publish-popup-title">Publish the result</h2>

            @if (error(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <p class="notice warn" role="status">
              Every member will see the first, second and third places. The result is frozen once
              published and cannot be changed here.
            </p>

            <div class="actions">
              <button class="btn" type="button" [disabled]="busy()" (click)="publishResult()">Publish</button>
              <button class="btn alt" type="button" (click)="closePopup()">Cancel</button>
            </div>
          </div>
        </div>
      }
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    /* The organiser sees the cover members will see. */
    .competition-cover {
      display: block;
      width: 100%;
      max-height: 240px;
      object-fit: cover;
      border-radius: var(--radius-md);
      margin-bottom: var(--space-3);
    }

    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .section-head h1,
    .section-head h2 {
      margin: 0;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
      align-items: center;
    }

    .inline-form {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex-wrap: wrap;
    }

    .modal-dialog.wide {
      max-width: 760px;
    }

    .details {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: var(--space-1) var(--space-3);
      margin: 0;
    }

    .details dt {
      font-weight: 600;
      color: var(--muted);
    }

    .details dd {
      margin: 0;
    }

    tr.podium td {
      font-weight: 600;
    }

    .podium-list {
      list-style: none;
      padding: 0;
      margin: var(--space-3) 0;
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }

    .podium-list li {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex-wrap: wrap;
    }
  `,
})
export class CompetitionDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);

  /** Names a locality "Name, City" - loaded once per session. */
  readonly localities = inject(LocalityOptions);

  /** Every active city or village, to name the one it is held in. */
  readonly cities = signal<readonly City[]>([]);

  /** Master data's Competition type list, to name the type. */
  readonly types = signal<readonly LookupItem[]>([]);

  readonly tabs: readonly { value: CompetitionTab; label: string }[] = [
    { value: 'details', label: 'Details' },
    { value: 'participants', label: 'Participants' },
    { value: 'judges', label: 'Judges' },
    { value: 'rounds', label: 'Rounds' },
    { value: 'scores', label: 'Scores' },
    { value: 'results', label: 'Results' },
  ];

  readonly tab = signal<CompetitionTab>('details');

  /** Which popup is open over the page, if any. */
  readonly popup = signal<'edit' | 'judge' | 'round' | 'confirm' | 'publish' | null>(null);

  readonly detail = signal<CompetitionDetail | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly selectedRoundId = signal<string | null>(null);
  readonly roundDetail = signal<RoundDetail | null>(null);
  readonly roundLoading = signal(false);
  readonly roundResult = signal<CompetitionResult | null>(null);

  /** The organiser's scoresheet, loaded when the Scores tab is opened. */
  readonly sheet = signal<Scoresheet | null>(null);
  readonly sheetLoading = signal(false);

  readonly finalResult = signal<CompetitionFinalResult | null>(null);

  /** What the confirmation popup is asking about. */
  private pending: { kind: 'move'; status: CompetitionStatus } | { kind: 'close'; round: Round } | null = null;

  readonly confirmTitle = signal('');
  readonly confirmText = signal('');

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  readonly members = computed(() =>
    [...this.names().entries()].map(([id, fullName]) => ({ id, fullName })),
  );

  readonly competition = computed<Competition | null>(() => this.detail()?.competition ?? null);
  readonly rounds = computed(() => this.detail()?.rounds ?? []);
  readonly judges = computed(() => this.detail()?.judges ?? []);
  readonly registered = computed(() => this.detail()?.registeredMemberIds ?? []);

  readonly selectedRound = computed(
    () => this.rounds().find((r) => r.id === this.selectedRoundId()) ?? null,
  );

  /** Members not already a judge, for the assign popup. */
  readonly judgeCandidates = computed(() => {
    const judging = new Set(this.judges().map((j) => j.memberId));

    return this.members().filter((m) => !judging.has(m.id));
  });

  readonly enteredMemberIds = computed(
    () => new Set((this.roundDetail()?.entries ?? []).map((e) => e.participantMemberId)),
  );

  readonly unenteredRegistrants = computed(() =>
    this.registered().filter((id) => !this.enteredMemberIds().has(id)),
  );

  /** Main rounds still taking marks - the result waits for none. */
  readonly roundsStillOpen = computed(
    () => this.rounds().filter((r) => r.kind === 'Main' && r.status === 'Open').length,
  );

  /** The details and the format can be corrected until registration closes, as the service allows. */
  readonly editable = computed(() => {
    const status = this.competition()?.status;

    return status === 'Draft' || status === 'RegistrationOpen';
  });

  /** The member whose panel is being changed; null when assigning somebody new. */
  readonly judgeEditing = signal<string | null>(null);

  judgeMemberId = '';
  judgePanel: JudgePanel = 'Main';
  entryMemberId = '';

  /** The one stage this competition can move to, or null when it is finished. */
  readonly nextStatus = computed<CompetitionStatus | null>(() => {
    switch (this.competition()?.status) {
      case 'Draft':
        return 'RegistrationOpen';
      case 'RegistrationOpen':
        return 'RegistrationClosed';
      case 'RegistrationClosed':
        return 'InProgress';
      case 'InProgress':
        return 'Closed';
      default:
        return null;
    }
  });

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.localities.load();
    this.api.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });
    this.api.listLookupItems('CompetitionType').subscribe({ next: (found) => this.types.set(found), error: () => {} });
    this.load();
  }

  // ---- Rendering ---------------------------------------------------------

  /**
   * Where it is held, as one line: the venue, then the locality ("Name, City")
   * or the city or village alone. Null when none of them is known.
   */
  whereHeld(subject: Competition): string | null {
    const area = subject.localityId
      ? this.localities.place(subject.localityId, subject.cityId)
      : (this.cities().find((c) => c.id === subject.cityId)?.name ?? null);

    const parts = [subject.venue, area].filter((part): part is string => !!part);

    return parts.length > 0 ? parts.join(' · ') : null;
  }

  typeName(subject: Competition): string | null {
    return this.types().find((t) => t.id === subject.competitionTypeId)?.label ?? null;
  }

  /** "Audition, then Round 1 to 3" - what starting judging will make. */
  plannedRounds(subject: Competition): string {
    const main = subject.roundCount === 0
      ? 'one judging'
      : subject.roundCount === 1
        ? 'Round 1'
        : `Round 1 to ${subject.roundCount}`;

    return subject.auditionRequired ? `the audition, then ${main}` : main;
  }

  countFor(tab: CompetitionTab): string {
    switch (tab) {
      case 'participants':
        return ` (${this.registered().length})`;
      case 'judges':
        return ` (${this.judges().length})`;
      case 'rounds':
        return this.rounds().length > 0 ? ` (${this.rounds().length})` : '';
      default:
        return '';
    }
  }

  stageLabel(status: CompetitionStatus): string {
    switch (status) {
      case 'RegistrationOpen':
        return 'Taking applications';
      case 'RegistrationClosed':
        return 'Applications closed';
      case 'InProgress':
        return 'Judging';
      default:
        return status;
    }
  }

  panelLabel(panel: JudgePanel): string {
    switch (panel) {
      case 'Audition':
        return 'Audition';
      case 'Both':
        return 'Audition and main rounds';
      default:
        return this.competition()?.auditionRequired ? 'Main rounds' : 'Judge';
    }
  }

  placeName(rank: number): string {
    return rank === 1 ? 'First' : rank === 2 ? 'Second' : 'Third';
  }

  /** The judges whose panel marks this round - the columns of its scoresheet. */
  judgesFor(round: Round, scores: Scoresheet): Scoresheet['judges'] {
    return scores.judges.filter((j) =>
      j.panel === 'Both' || (round.kind === 'Audition' ? j.panel === 'Audition' : j.panel === 'Main'));
  }

  markOf(entry: ScoresheetEntry, judgeMemberId: string): ScoresheetEntry['marks'][number] | null {
    return entry.marks.find((m) => m.judgeMemberId === judgeMemberId) ?? null;
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  moveLabel(next: CompetitionStatus): string {
    switch (next) {
      case 'RegistrationOpen':
        return 'Open registration';
      case 'RegistrationClosed':
        return 'Close registration';
      case 'InProgress':
        return 'Start judging';
      default:
        return 'Close competition';
    }
  }

  // ---- Tabs and popups ---------------------------------------------------

  showTab(tab: CompetitionTab): void {
    this.tab.set(tab);

    if (tab === 'scores') {
      this.loadSheet();
    }

    if (tab === 'results') {
      this.loadFinalResult();
    }
  }

  openPopup(which: 'edit' | 'publish'): void {
    this.error.set(null);
    this.done.set(null);
    this.popup.set(which);
  }

  closePopup(): void {
    this.popup.set(null);
    this.selectedRoundId.set(null);
    this.judgeEditing.set(null);
    this.pending = null;
    this.error.set(null);
  }

  onSaved(message: string): void {
    this.closePopup();
    this.done.set(message);
    this.load();
  }

  /** Assigning somebody new (null), or changing a judge's panel. */
  openJudge(memberId: string | null): void {
    this.error.set(null);
    this.done.set(null);
    this.judgeEditing.set(memberId);
    this.judgeMemberId = memberId ?? '';
    this.judgePanel = this.judges().find((j) => j.memberId === memberId)?.panel ?? 'Main';
    this.popup.set('judge');
  }

  showRound(round: Round): void {
    this.error.set(null);
    this.selectedRoundId.set(round.id);
    this.entryMemberId = '';
    this.popup.set('round');
    this.loadRound(round.id);
  }

  /** Moving on and closing a round each say what follows, before it happens. */
  askToMove(status: CompetitionStatus): void {
    const subject = this.competition();

    if (status === 'InProgress' && subject) {
      this.pending = { kind: 'move', status };
      this.confirmTitle.set('Start judging');
      this.confirmText.set(
        `This makes the rounds - ${this.plannedRounds(subject)} - and enters all ${subject.registrationCount} ` +
          `who applied${subject.auditionRequired ? ' in the audition' : ''}. Nobody can apply after this.`,
      );
      this.error.set(null);
      this.popup.set('confirm');
      return;
    }

    this.move(status);
  }

  askToClose(round: Round): void {
    if (round.kind !== 'Audition') {
      this.closeRound(round.id);
      return;
    }

    this.pending = { kind: 'close', round };
    this.confirmTitle.set('Close the audition');
    this.confirmText.set(
      `No more audition marks after this. The top ${this.competition()?.finalistCount ?? 'N'} by average ` +
        'go through to every round - and anybody tied with the last of them. Anybody no judge marked ' +
        'did not audition and does not go through.',
    );
    this.error.set(null);
    this.popup.set('confirm');
  }

  confirm(): void {
    const pending = this.pending;

    if (pending?.kind === 'move') {
      this.move(pending.status);
    } else if (pending?.kind === 'close') {
      this.closeRound(pending.round.id);
    }
  }

  // ---- Loading -----------------------------------------------------------

  private load(): void {
    this.loading.set(this.detail() === null);
    this.error.set(null);

    this.api.competition(this.id).subscribe({
      next: (found) => {
        this.detail.set(found);
        this.loading.set(false);
        this.loadNames();

        if (this.tab() === 'scores') {
          this.loadSheet();
        }

        if (this.tab() === 'results') {
          this.loadFinalResult();
        }
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.notFound.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  /** A failure leaves "A member" rather than a GUID, and is not an error. */
  private loadNames(): void {
    if (this.names().size > 0) {
      return;
    }

    this.api.listMembers().subscribe({
      next: (found) => this.names.set(new Map(found.map((m) => [m.id, m.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }

  private loadRound(roundId: string): void {
    this.roundLoading.set(true);
    this.roundResult.set(null);

    this.api.round(this.id, roundId).subscribe({
      next: (found) => {
        this.roundDetail.set(found);
        this.roundLoading.set(false);

        if (found.round.hasPublishedResult) {
          this.api.roundResult(this.id, roundId).subscribe({
            next: (result) => this.roundResult.set(result),
            error: () => this.roundResult.set(null),
          });
        }
      },
      error: () => {
        this.roundDetail.set(null);
        this.roundLoading.set(false);
      },
    });
  }

  private loadSheet(): void {
    this.sheetLoading.set(this.sheet() === null);

    this.api.scoresheet(this.id).subscribe({
      next: (found) => {
        this.sheet.set(found);
        this.sheetLoading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.sheetLoading.set(false);
      },
    });
  }

  /** A 404 before the result is published is the ordinary state, not an error. */
  private loadFinalResult(): void {
    if (!this.detail()?.hasPublishedResult) {
      this.finalResult.set(null);
      return;
    }

    this.api.competitionResult(this.id).subscribe({
      next: (found) => this.finalResult.set(found),
      error: () => this.finalResult.set(null),
    });
  }

  // ---- Acting ------------------------------------------------------------

  uploadPhoto(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    this.act(this.api.uploadCompetitionPhoto(this.id, file), 'Photo saved.');
    input.value = '';
  }

  removePhoto(): void {
    this.act(this.api.removeCompetitionPhoto(this.id), 'Photo removed.');
  }

  move(status: CompetitionStatus): void {
    this.act(
      this.api.moveCompetition(this.id, status),
      status === 'InProgress' ? 'Judging has started. Everybody who applied is entered.' : `Competition is now ${this.stageLabel(status)}.`,
    );
  }

  assignJudge(): void {
    if (!this.judgeMemberId) {
      return;
    }

    const who = this.memberName(this.judgeMemberId);
    const panel = this.competition()?.auditionRequired ? this.judgePanel : 'Main';
    const moving = this.judgeEditing() !== null;

    this.act(
      this.api.assignJudge(this.id, this.judgeMemberId, true, panel),
      moving ? `${who} now judges ${this.panelLabel(panel).toLowerCase()}.` : `${who} is now a judge.`,
    );
  }

  removeJudge(memberId: string): void {
    const who = this.memberName(memberId);

    this.act(this.api.assignJudge(this.id, memberId, false), `${who} is no longer a judge.`);
  }

  closeRound(roundId: string): void {
    const round = this.rounds().find((r) => r.id === roundId);

    this.act(
      this.api.closeRound(this.id, roundId),
      round?.kind === 'Audition' ? 'The audition is closed, and the finalists are in every round.' : `${round?.name ?? 'Round'} is closed.`,
    );
  }

  createEntry(roundId: string): void {
    if (!this.entryMemberId) {
      return;
    }

    const who = this.memberName(this.entryMemberId);
    const entryMemberId = this.entryMemberId;
    this.entryMemberId = '';

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.createEntry(this.id, roundId, entryMemberId).subscribe({
      next: () => {
        this.done.set(`${who} added.`);
        this.busy.set(false);
        this.load();
        this.loadRound(roundId);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  publish(roundId: string): void {
    this.act(this.api.publishRoundResults(this.id, roundId), 'The round result is published.');
  }

  publishResult(): void {
    this.act(this.api.publishCompetitionResult(this.id), 'The result is published. Every member can see the winners.');
  }

  /**
   * Every action closes its popup and re-reads the competition: an assign, a
   * move or a publish changes what the rest of the screen may offer, and the
   * server is the only thing that knows all of it at once. A refusal stays in
   * the popup that asked.
   */
  private act(work: { subscribe: (o: object) => void }, message: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.busy.set(false);
        this.closePopup();
        this.done.set(message);
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}

type CompetitionTab = 'details' | 'participants' | 'judges' | 'rounds' | 'scores' | 'results';
