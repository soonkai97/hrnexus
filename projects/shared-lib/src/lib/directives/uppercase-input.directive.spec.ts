import { Component } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UppercaseInputDirective } from './uppercase-input.directive';

/** Drive the directive through a real <input> so the HostListener('input') path runs. */
function typeInto(fixture: ComponentFixture<unknown>, selector: string, value: string): HTMLInputElement {
  const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
  return input;
}

@Component({
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, UppercaseInputDirective],
  template: `
    <input id="model" ncUppercase [(ngModel)]="model" />
    <input id="reactive" ncUppercase [formControl]="ctrl" />
  `,
})
class HostComponent {
  model = '';
  ctrl = new FormControl('');
}

describe('UppercaseInputDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('uppercases the value bound via ngModel', () => {
    const input = typeInto(fixture, '#model', 'nc001');
    expect(input.value).toBe('NC001');
    expect(host.model).toBe('NC001');
  });

  it('uppercases the value bound via a reactive FormControl', () => {
    const input = typeInto(fixture, '#reactive', 'abc123x');
    expect(input.value).toBe('ABC123X');
    expect(host.ctrl.value).toBe('ABC123X');
  });

  it('leaves already-uppercase / non-alpha input unchanged', () => {
    const input = typeInto(fixture, '#model', 'NC-001');
    expect(input.value).toBe('NC-001');
    expect(host.model).toBe('NC-001');
  });
});
