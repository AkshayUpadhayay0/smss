// Design tokens (match the smss-web violet theme). Every text/background pair used for body text is >= 4.5:1.
export const colors = {
  primary: '#6D28D9',
  primaryDeep: '#4C1D95',
  primaryText: '#5B21B6',
  tint: '#EDE9FE',
  tintBorder: '#C4B5FD',

  background: '#FAF9FF',
  surface: '#FFFFFF',
  border: '#E7E3F4',
  divider: '#EFECF8',

  ink: '#1E1B2E',
  inkSoft: '#3F3B57',
  muted: '#5F5B78',
  inputBorder: '#8F89AB',
  onPrimary: '#FFFFFF',

  success: '#047857',
  successTint: '#ECFDF5',
  successBorder: '#A7F3D0',

  danger: '#B91C1C',
  dangerTint: '#FEE2E2',

  warningText: '#92400E',
  warningTint: '#FEF3C7',
} as const;

export const radii = { card: 18, input: 14, button: 14, chip: 12, chipSmall: 10, pill: 999 } as const;

export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;

/** Minimum touch target (px). */
export const MIN_TOUCH = 44;

// Inter weights as loaded by @expo-google-fonts/inter (see app/_layout.tsx).
export const fonts = {
  regular: 'Inter_400Regular',
  medium: 'Inter_500Medium',
  semibold: 'Inter_600SemiBold',
  bold: 'Inter_700Bold',
} as const;

export const shadow = {
  card: {
    shadowColor: '#4C1D95',
    shadowOpacity: 0.06,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 4 },
    elevation: 2,
  },
} as const;
