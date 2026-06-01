import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { LocationsComponent } from './locations.component';
import { LocationStore, ToastService, ConfirmService } from 'shared-lib';

/**
 * LocationsComponent — venue CRUD.
 *
 * WHY these tests matter:
 *  - save() must call create vs update based on whether a location is being edited —
 *    mixing them up creates a duplicate instead of updating the existing record.
 *  - The empty-name guard prevents blank location names from reaching the API, which
 *    would break QR clock-in lookups that match on locationName.
 *  - remove() confirm ensures managers don't accidentally delete a location that still
 *    has active shifts or QR codes assigned.
 */

const fakeLocation = { id: 'loc-1', name: 'Main Bar' };

describe('LocationsComponent', () => {
  let component: LocationsComponent;

  let mockLocationStore: {
    load:        ReturnType<typeof vi.fn>;
    locations:   ReturnType<typeof vi.fn>;
    loading:     ReturnType<typeof vi.fn>;
    create:      ReturnType<typeof vi.fn>;
    update:      ReturnType<typeof vi.fn>;
    remove:      ReturnType<typeof vi.fn>;
    uploadImage: ReturnType<typeof vi.fn>;
    bulkCreate:  ReturnType<typeof vi.fn>;
  };
  let mockToastSvc:   { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockLocationStore = {
      load:        vi.fn(),
      locations:   vi.fn().mockReturnValue([]),
      loading:     vi.fn().mockReturnValue(false),
      create:      vi.fn().mockReturnValue(of(undefined)),
      update:      vi.fn().mockReturnValue(of(undefined)),
      remove:      vi.fn().mockReturnValue(of(undefined)),
      uploadImage: vi.fn().mockReturnValue(of(undefined)),
      bulkCreate:  vi.fn().mockReturnValue(of({ created: 2, skipped: 0 })),
    };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [LocationsComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: LocationStore,  useValue: mockLocationStore },
        { provide: ToastService,   useValue: mockToastSvc },
        { provide: ConfirmService, useValue: mockConfirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(LocationsComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── ngOnInit() ────────────────────────────────────────────────────────────────

  it('loads locations on init', () => {
    component.ngOnInit();
    expect(mockLocationStore.load).toHaveBeenCalled();
  });

  // ── save() — empty guard ───────────────────────────────────────────────────────

  describe('save() — empty-name guard', () => {
    it('does nothing when form.name is empty — blank names break QR clock-in lookups', () => {
      component.form.name = '';
      component.save();
      expect(mockLocationStore.create).not.toHaveBeenCalled();
      expect(mockLocationStore.update).not.toHaveBeenCalled();
    });

    it('does nothing when form.name is only whitespace', () => {
      component.form.name = '   ';
      component.save();
      expect(mockLocationStore.create).not.toHaveBeenCalled();
      expect(mockLocationStore.update).not.toHaveBeenCalled();
    });
  });

  // ── save() — create path ──────────────────────────────────────────────────────

  describe('save() — create path (editing is null)', () => {
    beforeEach(() => {
      component.form.name = 'VIP Lounge';
      component.editing.set(null);
    });

    it('calls store.create when no location is being edited', () => {
      component.save();
      expect(mockLocationStore.create).toHaveBeenCalledWith({ name: 'VIP LOUNGE' });
    });

    it('does NOT call store.update on the create path', () => {
      component.save();
      expect(mockLocationStore.update).not.toHaveBeenCalled();
    });

    it('shows success toast after creating', () => {
      component.save();
      expect(mockToastSvc.success).toHaveBeenCalledWith('locations.saved');
    });

    it('hides the form after creating', () => {
      component.showForm.set(true);
      component.save();
      expect(component.showForm()).toBe(false);
    });

    it('sets error signal and toast on create failure', () => {
      mockLocationStore.create.mockReturnValue(throwError(() => ({ error: { error: 'Name taken.' } })));
      component.save();
      expect(component.error()).toBe('Name taken.');
      expect(mockToastSvc.error).toHaveBeenCalledWith('Name taken.');
    });
  });

  // ── save() — update path ──────────────────────────────────────────────────────

  describe('save() — update path (editing is set)', () => {
    beforeEach(() => {
      component.form.name = 'Main Bar Renamed';
      component.editing.set(fakeLocation as any);
    });

    it('calls store.update with the editing location id and new form data', () => {
      component.save();
      expect(mockLocationStore.update).toHaveBeenCalledWith('loc-1', { name: 'MAIN BAR RENAMED' });
    });

    it('does NOT call store.create on the update path', () => {
      component.save();
      expect(mockLocationStore.create).not.toHaveBeenCalled();
    });

    it('shows success toast after updating', () => {
      component.save();
      expect(mockToastSvc.success).toHaveBeenCalledWith('locations.saved');
    });

    it('hides the form and clears editing state after updating', () => {
      component.showForm.set(true);
      component.save();
      expect(component.showForm()).toBe(false);
      expect(component.editing()).toBeNull();
    });
  });

  // ── remove() ─────────────────────────────────────────────────────────────────

  describe('remove()', () => {
    it('shows confirmation dialog before deleting', () => {
      component.remove('loc-1');
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('calls store.remove with the location id when confirmed', () => {
      component.remove('loc-1');
      expect(mockLocationStore.remove).toHaveBeenCalledWith('loc-1');
    });

    it('shows success toast after removal', () => {
      component.remove('loc-1');
      expect(mockToastSvc.success).toHaveBeenCalledWith('locations.deleted');
    });

    it('does NOT remove when confirmation is cancelled', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.remove('loc-1');
      expect(mockLocationStore.remove).not.toHaveBeenCalled();
    });

    it('sets error and shows error toast on removal failure', () => {
      mockLocationStore.remove.mockReturnValue(throwError(() => ({ error: { error: 'Location in use.' } })));
      component.remove('loc-1');
      expect(component.error()).toBe('Location in use.');
      expect(mockToastSvc.error).toHaveBeenCalledWith('Location in use.');
    });
  });

  // ── saveBulk() — bulk add ──────────────────────────────────────────────────────

  describe('saveBulk()', () => {
    it('parses one name per line (trims, drops blanks) and calls store.bulkCreate', () => {
      component.bulkText = '  Main Bar \n\nRooftop\n   ';
      component.saveBulk();
      expect(mockLocationStore.bulkCreate).toHaveBeenCalledWith(['Main Bar', 'Rooftop']);
    });

    it('does nothing when the textarea has no usable names', () => {
      component.bulkText = '   \n  ';
      component.saveBulk();
      expect(mockLocationStore.bulkCreate).not.toHaveBeenCalled();
    });

    it('shows a created/skipped toast and closes the dialog on success', () => {
      component.bulkText = 'Main Bar\nRooftop';
      component.showBulk.set(true);
      component.saveBulk();
      expect(mockToastSvc.success).toHaveBeenCalledWith('locations.bulkAddedToast');
      expect(component.showBulk()).toBe(false);
    });

    it('shows an error toast on failure', () => {
      mockLocationStore.bulkCreate.mockReturnValue(throwError(() => ({ error: { error: 'Bad.' } })));
      component.bulkText = 'Main Bar';
      component.saveBulk();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Bad.');
    });
  });

  // ── uploadImage() ─────────────────────────────────────────────────────────────

  describe('uploadImage()', () => {
    it('calls store.uploadImage with the location id and selected file', () => {
      const file = new File(['data'], 'photo.jpg', { type: 'image/jpeg' });
      const event = { target: { files: [file] } } as unknown as Event;
      component.uploadImage(fakeLocation as any, event);
      expect(mockLocationStore.uploadImage).toHaveBeenCalledWith('loc-1', file);
    });

    it('does nothing when no file is selected', () => {
      const event = { target: { files: [] } } as unknown as Event;
      component.uploadImage(fakeLocation as any, event);
      expect(mockLocationStore.uploadImage).not.toHaveBeenCalled();
    });

    it('shows success toast after image upload', () => {
      const file = new File(['data'], 'photo.jpg', { type: 'image/jpeg' });
      const event = { target: { files: [file] } } as unknown as Event;
      component.uploadImage(fakeLocation as any, event);
      expect(mockToastSvc.success).toHaveBeenCalledWith('locations.imageUploaded');
    });

    it('shows error toast when upload fails', () => {
      const file = new File(['data'], 'photo.jpg', { type: 'image/jpeg' });
      const event = { target: { files: [file] } } as unknown as Event;
      mockLocationStore.uploadImage.mockReturnValue(throwError(() => ({ error: { error: 'Too large.' } })));
      component.uploadImage(fakeLocation as any, event);
      expect(mockToastSvc.error).toHaveBeenCalledWith('Too large.');
    });
  });
});
