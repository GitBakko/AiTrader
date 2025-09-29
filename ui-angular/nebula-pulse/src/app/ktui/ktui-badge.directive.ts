import { Directive, HostBinding, Input } from '@angular/core';

type KtuiBadgeVariant = 'success' | 'warning' | 'danger' | 'info' | '' | null | undefined;

@Directive({
  selector: '[ktuiBadge]',
  standalone: true
})
export class KtuiBadgeDirective {
  @HostBinding('class.ktui-badge') baseClass = true;
  @HostBinding('class.ktui-badge--success') success = false;
  @HostBinding('class.ktui-badge--warning') warning = false;
  @HostBinding('class.ktui-badge--danger') danger = false;
  @HostBinding('class.ktui-badge--info') info = false;

  @Input('ktuiBadge')
  set variant(value: KtuiBadgeVariant) {
    this.reset();
    switch (value) {
      case 'success':
        this.success = true;
        break;
      case 'warning':
        this.warning = true;
        break;
      case 'danger':
        this.danger = true;
        break;
      case 'info':
        this.info = true;
        break;
      default:
        break;
    }
  }

  private reset(): void {
    this.success = false;
    this.warning = false;
    this.danger = false;
    this.info = false;
  }
}
