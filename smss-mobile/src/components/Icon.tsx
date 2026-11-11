import { Feather } from '@expo/vector-icons';
import type { ComponentProps } from 'react';
import { colors } from '../theme/tokens';

export type IconName = ComponentProps<typeof Feather>['name'];

/** Feather icons only. Decorative by default; give the parent an accessibilityLabel instead. */
export function Icon({ name, size = 20, color = colors.inkSoft }: { name: IconName; size?: number; color?: string }) {
  return <Feather name={name} size={size} color={color} accessibilityElementsHidden importantForAccessibility="no" />;
}
