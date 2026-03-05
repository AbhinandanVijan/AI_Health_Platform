import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = () => {
  const router = inject(Router);
  const token = localStorage.getItem('aihealth.jwt');
  if (!token) {
    router.navigate(['/login']);
    return false;
  }
  return true;
};

export const guestGuard: CanActivateFn = () => {
  const router = inject(Router);
  const token = localStorage.getItem('aihealth.jwt');
  if (token) {
    router.navigate(['/dashboard']);
    return false;
  }
  return true;
};

export const clinicianGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);

  if (!auth.isAuthenticated()) {
    router.navigate(['/login']);
    return false;
  }

  const currentMe = auth.me();
  if (currentMe) {
    const isClinician = currentMe.roles?.includes('Clinician');
    if (!isClinician) {
      router.navigate(['/dashboard']);
      return false;
    }
    return true;
  }

  return auth.loadMe().pipe(
    map((me) => {
      const isClinician = me.roles?.includes('Clinician');
      if (!isClinician) {
        router.navigate(['/dashboard']);
        return false;
      }
      return true;
    }),
    catchError(() => {
      router.navigate(['/login']);
      return of(false);
    }),
  );
};
