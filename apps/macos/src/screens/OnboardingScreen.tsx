import React from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { colors, radii, spacing, typography } from '@vaaluation/ui';
import { useSettings } from '../state/SettingsContext';
import { usePermissions } from '../hooks/usePermissions';
import { requestAccessibility } from '../native/VLPermissions';

export function OnboardingScreen({ onDone }: { onDone: () => void }) {
  const { update } = useSettings();
  const { status, refresh } = usePermissions();

  return (
    <ScrollView style={styles.container} contentContainerStyle={styles.content}>
      <Text style={styles.heading}>Welcome to ExileWorth</Text>
      <View style={styles.step}>
        <Text style={styles.stepTitle}>Accessibility permission</Text>
        {status?.accessibility === 'granted' ? (
          <Text style={styles.grantedText}>
            Granted — ExileWorth is ready to send the item-copy keystroke.
          </Text>
        ) : (
          <View>
            <Text style={styles.stepBody}>
              Allow ExileWorth to send the item-copy keystroke when you use the
              price-check shortcut. In System Settings → Privacy &amp; Security, turn on
              ExileWorth under Accessibility (called Device Control and Data Access on
              newer macOS versions).
            </Text>
            <Pressable
              style={styles.secondaryButton}
              onPress={() => {
                requestAccessibility().catch(() => {});
              }}
            >
              <Text style={styles.secondaryButtonText}>Set Up Accessibility…</Text>
            </Pressable>
            <Pressable style={styles.secondaryButton} onPress={refresh}>
              <Text style={styles.secondaryButtonText}>Check Permission Again</Text>
            </Pressable>
          </View>
        )}
      </View>

      <Text style={styles.stepBody}>
        Hover over an item in Path of Exile and press Control + D to check its price. You
        can also paste an item into the desktop app to check it manually.
      </Text>

      <Pressable
        style={styles.button}
        onPress={() => {
          update({ onboardingCompleted: true });
          onDone();
        }}
      >
        <Text style={styles.buttonText}>
          {status?.accessibility === 'granted'
            ? 'Get Started'
            : 'Continue Without Shortcut'}
        </Text>
      </Pressable>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  content: {
    padding: spacing.xl,
    maxWidth: 560,
    alignSelf: 'center',
  },
  heading: {
    color: colors.textPrimary,
    fontSize: typography.sizeHeading,
    fontWeight: '700',
    marginBottom: spacing.xl,
  },
  step: {
    marginBottom: spacing.lg,
    backgroundColor: colors.charcoal,
    borderColor: colors.gold,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radii.md,
    padding: spacing.lg,
  },
  stepTitle: {
    color: colors.textSecondary,
    fontSize: typography.sizeTitle,
    fontWeight: '600',
    marginBottom: spacing.xs,
  },
  stepBody: {
    color: colors.textPrimary,
    fontSize: typography.sizeBody,
    lineHeight: 19,
  },
  grantedText: {
    color: colors.success,
    fontSize: typography.sizeBody,
  },
  secondaryButton: {
    marginTop: spacing.md,
    alignSelf: 'flex-start',
    backgroundColor: colors.obsidian,
    borderColor: colors.gold,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radii.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  secondaryButtonText: {
    color: colors.textPrimary,
    fontSize: typography.sizeBody,
  },
  button: {
    marginTop: spacing.md,
    backgroundColor: colors.vaalRed,
    borderRadius: radii.md,
    paddingVertical: spacing.md,
    alignItems: 'center',
  },
  buttonText: {
    color: colors.textPrimary,
    fontSize: typography.sizeTitle,
    fontWeight: '600',
  },
});
