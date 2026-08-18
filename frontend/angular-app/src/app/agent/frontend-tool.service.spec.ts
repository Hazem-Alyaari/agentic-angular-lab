import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';
import { FrontendToolService } from './frontend-tool.service';

describe('FrontendToolService', () => {
  let service: FrontendToolService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), FrontendToolService]
    });
    service = TestBed.inject(FrontendToolService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
  });

  it('navigates to /employees/:id for a valid employeeId', async () => {
    const result = await service.execute('navigate_to_employee', { employeeId: 101 });

    expect(router.navigate).toHaveBeenCalledWith(['/employees', 101]);
    expect(result).toEqual({
      success: true,
      employeeId: 101,
      route: '/employees/101'
    });
  });

  it('does not navigate when employeeId is invalid', async () => {
    const result = await service.execute('navigate_to_employee', {
      employeeId: 'abc'
    });

    expect(router.navigate).not.toHaveBeenCalled();
    expect(result).toEqual({
      success: false,
      error: 'Invalid employee ID'
    });
  });

  it('rejects unknown tools without executing anything', async () => {
    const result = await service.execute('delete_everything', {});

    expect(router.navigate).not.toHaveBeenCalled();
    expect(result).toEqual({
      success: false,
      error: 'unsupported tool'
    });
  });
});
