import { Routes } from '@angular/router';

export const routes: Routes = [
	{ path: '', redirectTo: 'desk', pathMatch: 'full' },
	{
		path: 'desk',
		loadComponent: () =>
			import('./features/trader-desk/trader-desk.component').then((m) => m.TraderDeskComponent)
	},
	{
		path: 'risk',
		loadComponent: () =>
			import('./features/risk-console/risk-console.component').then((m) => m.RiskConsoleComponent)
	},
	{
		path: 'backtest',
		loadComponent: () =>
			import('./features/backtest-lab/backtest-lab.component').then((m) => m.BacktestLabComponent)
	},
	{ path: '**', redirectTo: 'desk' }
];
