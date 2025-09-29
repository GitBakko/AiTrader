import { Directive, HostBinding, Input } from '@angular/core';

type KtuiPillVariant = 'primary' | 'danger' | 'muted' | 'warning' | '' | null | undefined;

@Directive({
  selector: '[ktuiPill]',
  standalone: true
})
export class KtuiPillDirective {
  @HostBinding('class.ktui-pill') baseClass = true;
  @HostBinding('class.ktui-pill--primary') primary = false;
  @HostBinding('class.ktui-pill--danger') danger = false;
  @HostBinding('class.ktui-pill--muted') muted = false;
  @HostBinding('class.ktui-pill--warning') warning = false;

  @Input('ktuiPill')
  set variant(value: KtuiPillVariant) {
    this.reset();
    switch (value) {
      case 'primary':
        this.primary = true;
        break;
      case 'danger':
        this.danger = true;
        break;
      case 'muted':
        this.muted = true;
        break;
      case 'warning':
        this.warning = true;
        break;
      default:
        break;
    }
  }

  private reset(): void {
    this.primary = false;
    this.danger = false;
    this.muted = false;
    this.warning = false;
  }
}
