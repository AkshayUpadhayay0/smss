// Mirrors ReportCardSettingsDtos.cs (Examination / Report Card, stage 5a: settings, branding and the design chooser).

export interface ReportCardDesign {
  code: string; name: string; description: string; modes: string[]; orientation: string;
  /** False = not built yet: shown as "coming soon", never selectable. */
  available: boolean;
  /** True when the design has a photo slot: the "show photo" switch only matters for these designs. */
  supportsPhoto?: boolean;
}

export interface ReportCardSchemeChoice { schemeId: number; schemeName: string; evaluationMode: string; templateKey: string; classNames: string[] }

export interface ReportCardSettings {
  primaryColor: string; secondaryColor: string;
  headerLine?: string | null; recognitionText?: string | null; udiseCode?: string | null; footerText?: string | null; rulesText?: string | null;
  hasSignature: boolean; showCover: boolean; showRules: boolean; showAttendance: boolean; showGradeLegend: boolean; showPhoto?: boolean;
  isDefault: boolean; updatedAt?: string | null; schoolName: string; hasLogo: boolean;
  designs: ReportCardDesign[]; schemes: ReportCardSchemeChoice[]; defaultPrimaryColor: string; defaultSecondaryColor: string;
}

export interface ReportCardSettingsRequest {
  primaryColor: string; secondaryColor: string;
  headerLine: string | null; recognitionText: string | null; udiseCode: string | null; footerText: string | null; rulesText: string | null;
  showCover: boolean; showRules: boolean; showAttendance: boolean; showGradeLegend: boolean; showPhoto: boolean;
}

/** `mode` only matters for a design that supports both (Board Style, Minimal): which kind of sample to draw. A design for one mode always gets its own sample. */
export interface ReportCardPreviewRequest { templateKey: string; settings: ReportCardSettingsRequest; terms: number; mode?: 'MARKS' | 'GRADES' }
export interface ReportCardPreview { templateKey: string; isSample: boolean; pageCount: number; pages: string[] }
export interface ReportCardSignatureState { hasSignature: boolean }

/** One suggested part when a version has more cards than a single bulk file may hold (HTTP 409 from the bulk endpoint). */
export interface BulkPart { fromRoll: string; toRoll: string; count: number }
export interface BulkTooLarge { total: number; maxPerRequest: number; parts: BulkPart[] }

export const HEX_COLOR = /^#[0-9A-Fa-f]{6}$/;
export const RC_LIMITS = { headerLine: 150, recognitionText: 150, udiseCode: 20, footerText: 200, rulesText: 1500 } as const;
