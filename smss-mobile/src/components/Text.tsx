import { Text as RNText, type TextProps as RNTextProps, type TextStyle } from 'react-native';
import { colors, fonts } from '../theme/tokens';

export type TextVariant = 'display' | 'title' | 'heading' | 'body' | 'small' | 'label' | 'tab';

const variants: Record<TextVariant, TextStyle> = {
  display: { fontFamily: fonts.bold, fontSize: 28, lineHeight: 34, color: colors.ink },
  title: { fontFamily: fonts.bold, fontSize: 22, lineHeight: 28, color: colors.ink },
  heading: { fontFamily: fonts.semibold, fontSize: 17, lineHeight: 23, color: colors.ink },
  body: { fontFamily: fonts.regular, fontSize: 15, lineHeight: 22, color: colors.inkSoft },
  small: { fontFamily: fonts.regular, fontSize: 13, lineHeight: 18, color: colors.muted },
  label: { fontFamily: fonts.semibold, fontSize: 13, lineHeight: 18, color: colors.inkSoft },
  tab: { fontFamily: fonts.semibold, fontSize: 11, lineHeight: 14, color: colors.muted },
};

export interface TextProps extends RNTextProps {
  variant?: TextVariant;
  color?: string;
  weight?: 'regular' | 'medium' | 'semibold' | 'bold';
}

/** Every piece of text in the app goes through this, so Inter and the contrast-checked colours apply everywhere. */
export function Text({ variant = 'body', color, weight, style, ...rest }: TextProps) {
  return <RNText {...rest} style={[variants[variant], color ? { color } : null, weight ? { fontFamily: fonts[weight] } : null, style]} />;
}
