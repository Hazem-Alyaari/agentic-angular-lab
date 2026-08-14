import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit {
  title = 'Agentic Angular Lab';
  healthStatus = 'checking';

  private readonly http = inject(HttpClient);

  ngOnInit(): void {
    this.http.get<{ status: string }>('/api/health').subscribe({
      next: (response) => {
        this.healthStatus = response.status;
      },
      error: () => {
        this.healthStatus = 'unavailable';
      }
    });
  }
}
