import { Routes } from '@angular/router';
import { LoginComponent } from './features/auth/login.component';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { UploadsComponent } from './features/uploads/uploads.component';
import { HistoryComponent } from './features/history/history.component';
import { ClinicianReviewComponent } from './features/clinician/clinician-review.component';
import { PatientReviewHistoryComponent } from './features/clinician/patient-review-history.component';
import { ProfileComponent } from './features/profile/profile.component';
import { ShellComponent } from './layout/shell.component';
import { authGuard, clinicianGuard, guestGuard, homeGuard } from './core/guards/auth.guard';

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
			{ path: 'profile', component: ProfileComponent },
			{ path: 'clinician-review', component: ClinicianReviewComponent, canActivate: [clinicianGuard] },
			{ path: 'review-history', component: PatientReviewHistoryComponent, canActivate: [clinicianGuard] },
			{ path: '', pathMatch: 'full', canActivate: [homeGuard], component: DashboardComponent },
		],
	},
	{ path: '**', redirectTo: '' },
];
