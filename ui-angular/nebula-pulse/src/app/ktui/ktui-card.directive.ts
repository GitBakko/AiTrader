import { Directive, HostBinding, Input } from '@angular/core';

type KtuiCardVariant = 'glass' | 'accent' | 'surface' | '' | null | undefined;

@Directive({
  selector: '[ktuiCard]',
  standalone: true
})
export class KtuiCardDirective {
  @HostBinding('class.ktui-card') baseClass = true;
  @HostBinding('class.ktui-card--glass') glass = false;
  @HostBinding('class.ktui-card--accent') accent = false;
  @HostBinding('class.ktui-card--surface') surface = false;

  @Input('ktuiCard')
  set variant(value: KtuiCardVariant) {
    this.reset();
    switch (value) {
      case 'glass':
        this.glass = true;
        break;
      case 'accent':
        this.accent = true;
        break;
      case 'surface':
        this.surface = true;
        break;
      default:
        break;
    }
  }

  private reset(): void {
    this.glass = false;
    this.accent = false;
    this.surface = false;
  }
}
