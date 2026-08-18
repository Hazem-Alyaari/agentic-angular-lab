import { Routes } from '@angular/router';
import { EmployeeProfileComponent } from './employees/employee-profile.component';

export const routes: Routes = [
  {
    path: 'employees/:id',
    component: EmployeeProfileComponent
  }
];
