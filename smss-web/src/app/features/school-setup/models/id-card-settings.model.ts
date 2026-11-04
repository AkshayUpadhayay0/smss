/** Mirrors IdCardTemplateInfoDto: one of the built-in ID card designs and the colours it ships with. */
export interface IdCardTemplateInfo {
  code: string;
  name: string;
  description: string;
  defaultPrimaryColor: string;
  defaultSecondaryColor: string;
}

/** Mirrors IdCardSettingsDto. With no saved row the server returns the defaults and `isDefault` is true. */
export interface IdCardSettings {
  templateCode: string;
  primaryColor: string;
  secondaryColor: string;
  tagline: string | null;
  website: string | null;
  email: string | null;
  termsText: string | null;
  /** A principal signature is on file (the file itself is private and never sent). */
  hasSignature: boolean;
  showDob: boolean;
  showAddress: boolean;
  showBloodGroup: boolean;
  showMotherName: boolean;
  isDefault: boolean;
  updatedAt: string | null;
  templates: IdCardTemplateInfo[];
}

/** Mirrors IdCardSettingsRequest / IdCardPreviewRequest. A null colour means "the template's own default". */
export interface IdCardSettingsRequest {
  templateCode: string;
  primaryColor: string | null;
  secondaryColor: string | null;
  tagline: string | null;
  website: string | null;
  email: string | null;
  termsText: string | null;
  showDob: boolean;
  showAddress: boolean;
  showBloodGroup: boolean;
  showMotherName: boolean;
}

export interface IdCardPreviewRequest extends IdCardSettingsRequest {
  studentId?: string | null;
}

/** Mirrors IdCardPreviewDto: both faces as PNG (base64). */
export interface IdCardPreview {
  templateCode: string;
  frontPng: string;
  backPng: string;
  isSample: boolean;
}

export interface IdCardSignatureState {
  hasSignature: boolean;
}

export const HEX_COLOR = /^#[0-9A-Fa-f]{6}$/;
export const MAX_TERMS_LENGTH = 300;
export const MAX_TERMS_LINES = 5;
