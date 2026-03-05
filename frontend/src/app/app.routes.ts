import { Routes } from '@angular/router';
import { LoginComponent } from './features/auth/login.component';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { UploadsComponent } from './features/uploads/uploads.component';
import { HistoryComponent } from './features/history/history.component';
import { ClinicianReviewComponent } from './features/clinician/clinician-review.component';
import { ShellComponent } from './layout/shell.component';
import { authGuard, clinicianGuard, guestGuard } from './core/guards/auth.guard';

export const routes: Routes = [
	{
		path: 'login',
		component: LoginComponent,
		canActivate: [guestGuard],
	},
	{
		path: '',
		component: ShellComponent,
		canActivate: [authGuard],
		children: [
			{ path: 'dashboard', component: DashboardComponent },
			{ path: 'uploads', component: UploadsComponent },
			{ path: 'history', component: HistoryComponent },
			{ path: 'clinician-review', component: ClinicianReviewComponent, canActivate: [clinicianGuard] },
			{ path: '', redirectTo: 'dashboard', pathMatch: 'full' },
		],
	},
	{ path: '**', redirectTo: '' },
];
