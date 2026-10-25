import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService, AuthedImageDirective } from '@samaajconnect/shared';
import { MemberLookup, initialsOf } from './member-lookup';
import { Child, Family, FamilyMember } from './members.models';

/** One person on the tree: a member with a profile, or a minor's record. */
export interface TreePerson {
  readonly key: string;
  readonly name: string;

  /** A member's profile id - null for a minor's record, which has no profile page. */
  readonly memberId: string | null;
  readonly photoUrl: string | null;
  readonly note: string | null;
  readonly isYou: boolean;
  readonly isHead: boolean;
  readonly isAdmin: boolean;
}

/** The household as three generations around its admin, and whoever else lives in it. */
export interface Tree {
  readonly parents: readonly TreePerson[];
  readonly admin: TreePerson | null;
  readonly spouse: TreePerson | null;
  readonly siblings: readonly TreePerson[];
  readonly children: readonly TreePerson[];
  readonly others: readonly TreePerson[];
}

/**
 * The household as a family tree, on My Family.
 *
 * **It is drawn around the family admin, because that is what the data
 * records.** A member joins by naming their relationship *to the admin* -
 * Parent, Spouse, Sibling, Child or Other - so the tree can place the admin's
 * parents above them, their spouse beside them, their siblings either side, and
 * their children below. It cannot place anybody's relatives but the admin's: a
 * sibling's spouse joined as "Other", and that is all the household knows. So
 * "Other" members are listed under the tree rather than guessed onto it.
 *
 * Minors are the household's child records, beneath the admin with the adult
 * children. A child who has converted to an account of their own is already a
 * member - relationship Child - so their old record is not drawn twice.
 *
 * Faces come from the bounded lookup every member screen here uses, one call
 * per adult on the tree; a minor's photo is the household's own, already
 * fetched with the token like any other.
 */
@Component({
  selector: 'app-family-tree',
  imports: [AuthedImageDirective, NgTemplateOutlet, RouterLink],
  styleUrl: './family-tree.css',
  template: `
    @let shape = tree();

    <div class="tree" role="group" aria-label="Family tree">
      @if (shape.parents.length > 0) {
        <ol class="generation" aria-label="Parents">
          @for (person of shape.parents; track person.key) {
            <li><ng-container *ngTemplateOutlet="node; context: { $implicit: person }" /></li>
          }
        </ol>
        <div class="link down" aria-hidden="true"></div>
      }

      <ol class="generation" aria-label="The family admin's generation">
        @for (person of shape.siblings.slice(0, half(shape.siblings)); track person.key) {
          <li><ng-container *ngTemplateOutlet="node; context: { $implicit: person }" /></li>
        }
        @if (shape.admin; as admin) {
          <li class="couple">
            <ng-container *ngTemplateOutlet="node; context: { $implicit: admin }" />
            @if (shape.spouse; as spouse) {
              <span class="bond" aria-hidden="true"></span>
              <ng-container *ngTemplateOutlet="node; context: { $implicit: spouse }" />
            }
          </li>
        }
        @for (person of shape.siblings.slice(half(shape.siblings)); track person.key) {
          <li><ng-container *ngTemplateOutlet="node; context: { $implicit: person }" /></li>
        }
      </ol>

      @if (shape.children.length > 0) {
        <div class="link down" aria-hidden="true"></div>
        <ol class="generation children" aria-label="Children">
          @for (person of shape.children; track person.key) {
            <li><ng-container *ngTemplateOutlet="node; context: { $implicit: person }" /></li>
          }
        </ol>
      }
    </div>

    @if (shape.others.length > 0) {
      <h3 class="others-heading">Also in the household</h3>
      <ul class="generation others">
        @for (person of shape.others; track person.key) {
          <li><ng-container *ngTemplateOutlet="node; context: { $implicit: person }" /></li>
        }
      </ul>
    }

    <ng-template #node let-person>
      <div class="person" [class.you]="person.isYou">
        @if (person.photoUrl) {
          <img class="face" [scAuthedSrc]="person.photoUrl" alt="" />
        } @else {
          <span class="face initials" aria-hidden="true">{{ initials(person.name) }}</span>
        }
        @if (person.memberId) {
          <a class="name" [routerLink]="['/members', person.memberId]">{{ person.name }}</a>
        } @else {
          <span class="name">{{ person.name }}</span>
        }
        @if (person.note) {
          <span class="small">{{ person.note }}</span>
        }
        <span class="tags">
          @if (person.isYou) { <span class="pill">You</span> }
          @if (person.isHead) { <span class="pill ok">Head</span> }
          @if (person.isAdmin) { <span class="pill">Family admin</span> }
        </span>
      </div>
    </ng-template>
  `,
})
export class FamilyTreeComponent {
  private readonly lookup = inject(MemberLookup);
  private readonly auth = inject(AuthService);

  readonly family = input.required<Family>();
  readonly children = input<readonly Child[]>([]);

  constructor() {
    // Faces for every adult on the tree - the bounded, memoized lookup.
    effect(() => this.lookup.resolve(this.activeMembers().map((m) => m.memberProfileId)));
  }

  private readonly activeMembers = computed(() =>
    this.family().members.filter((m) => m.status === 'Active'),
  );

  readonly tree = computed<Tree>(() => {
    const family = this.family();
    const me = this.auth.user()?.userId ?? null;
    const active = this.activeMembers();

    const person = (m: FamilyMember, note: string | null = null): TreePerson => ({
      key: m.id,
      name: m.fullName,
      memberId: m.memberProfileId,
      photoUrl: this.lookup.member(m.memberProfileId)?.photoUrl ?? null,
      note,
      isYou: m.memberProfileId === me,
      isHead: m.memberProfileId === family.familyHeadMemberId,
      isAdmin: m.memberProfileId === family.familyAdminMemberId,
    });

    const admin = active.find((m) => m.memberProfileId === family.familyAdminMemberId) ?? null;
    const others = active.filter((m) => m !== admin);
    const having = (relationship: FamilyMember['relationship']) =>
      others.filter((m) => m.relationship === relationship);

    // A converted child is a member now, relationship Child; their record is history.
    const minors: TreePerson[] = this.children()
      .filter((c) => c.status === 'Minor')
      .map((c) => ({
        key: 'child-' + c.id,
        name: c.fullName,
        memberId: null,
        photoUrl: c.photoUrl,
        note: `Age ${c.age}`,
        isYou: false,
        isHead: false,
        isAdmin: false,
      }));

    return {
      parents: having('Parent').map((m) => person(m)),
      admin: admin ? person(admin) : null,
      // The first spouse sits beside the admin; a household recording two is
      // unusual enough that the second is listed under the tree, not dropped.
      spouse: having('Spouse')[0] ? person(having('Spouse')[0]!) : null,
      siblings: having('Sibling').map((m) => person(m)),
      children: [...having('Child').map((m) => person(m)), ...minors],
      others: [...having('Spouse').slice(1), ...having('Other')].map((m) => person(m)),
    };
  });

  /** Siblings split either side of the admin, the extra one to the left. */
  half(siblings: readonly TreePerson[]): number {
    return Math.ceil(siblings.length / 2);
  }

  initials(name: string): string {
    return initialsOf(name) || '?';
  }
}
