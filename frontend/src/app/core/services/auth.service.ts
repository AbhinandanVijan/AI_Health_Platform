import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse, MeResponse, RegisterRequest } from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenKey = 'aihealth.jwt';
  private readonly meSignal = signal<MeResponse | null>(null);

  constructor(private readonly http: HttpClient) {}

  get token(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  get me() {
    return this.meSignal.asReadonly();
  }

  isAuthenticated(): boolean {
    return !!this.token;
  }

  login(payload: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/login', payload).pipe(
      tap((res) => localStorage.setItem(this.tokenKey, res.token))
    );
  }

  register(payload: RegisterRequest): Observable<unknown> {
    return this.http.post('/api/auth/register', payload);
  }

  loadMe(): Observable<MeResponse> {
    return this.http.get<MeResponse>('/api/me').pipe(
      tap((me) => this.meSignal.set(me))
    );
  }

  logout(): void {
    localStorage.removeItem(this.tokenKey);
    this.meSignal.set(null);
  }
}
