import { Component } from '@angular/core';
import { NebulaShellComponent } from './layout/nebula-shell.component';

@Component({
	selector: 'app-root',
	standalone: true,
	imports: [NebulaShellComponent],
	template: '<nebula-shell />'
})
export class App {}
