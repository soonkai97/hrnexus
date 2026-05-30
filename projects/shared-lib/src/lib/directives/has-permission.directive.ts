import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { PermissionService, PermissionKey } from '../services/permission.service';

/**
 * Structural directive that renders its content only when the user holds the given permission.
 *
 *   <button *ncHasPermission="'staff.manage'">Add Staff</button>
 *
 * Reactive: re-evaluates when the loaded permission set changes (e.g. after load()).
 */
@Directive({ selector: '[ncHasPermission]', standalone: true })
export class HasPermissionDirective {
  private readonly perms = inject(PermissionService);
  private readonly tpl   = inject(TemplateRef<unknown>);
  private readonly vcr   = inject(ViewContainerRef);

  readonly ncHasPermission = input.required<PermissionKey>();

  private rendered = false;

  constructor() {
    effect(() => {
      const allowed = this.perms.has(this.ncHasPermission());
      if (allowed && !this.rendered) {
        this.vcr.createEmbeddedView(this.tpl);
        this.rendered = true;
      } else if (!allowed && this.rendered) {
        this.vcr.clear();
        this.rendered = false;
      }
    });
  }
}
