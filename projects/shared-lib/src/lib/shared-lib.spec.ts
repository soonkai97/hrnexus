import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SharedLib } from './shared-lib';

describe('SharedLib', () => {
  let component: SharedLib;
  let fixture: ComponentFixture<SharedLib>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SharedLib],
    }).compileComponents();

    fixture = TestBed.createComponent(SharedLib);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
