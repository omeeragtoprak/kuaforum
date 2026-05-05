import { forwardRef, useState } from 'react';
import { TextInput, TextInputProps, StyleSheet, View, Text, Pressable } from 'react-native';
import { Colors, Radius, Spacing, FontSize, FontWeight } from '@/lib/theme';

type InputProps = TextInputProps & {
  label?: string;
  error?: string;
  hint?: string;
  leftSlot?: React.ReactNode;
  rightSlot?: React.ReactNode;
  required?: boolean;
};

export const Input = forwardRef<TextInput, InputProps>(function Input(
  { label, error, hint, leftSlot, rightSlot, required, style, onFocus, onBlur, ...rest },
  ref,
) {
  const [focused, setFocused] = useState(false);
  return (
    <View style={styles.wrapper}>
      {label ? (
        <Text style={styles.label}>
          {label}
          {required ? <Text style={styles.required}> *</Text> : null}
        </Text>
      ) : null}
      <View
        style={[
          styles.field,
          focused && styles.fieldFocused,
          !!error && styles.fieldError,
        ]}
      >
        {leftSlot ? <View style={styles.slot}>{leftSlot}</View> : null}
        <TextInput
          ref={ref}
          {...rest}
          placeholderTextColor={Colors.textMuted}
          onFocus={(e) => { setFocused(true); onFocus?.(e); }}
          onBlur={(e) => { setFocused(false); onBlur?.(e); }}
          style={[styles.input, style]}
        />
        {rightSlot ? <View style={styles.slot}>{rightSlot}</View> : null}
      </View>
      {error ? (
        <Text style={styles.error}>{error}</Text>
      ) : hint ? (
        <Text style={styles.hint}>{hint}</Text>
      ) : null}
    </View>
  );
});

const styles = StyleSheet.create({
  wrapper: { marginBottom: Spacing.lg },
  label: {
    fontSize: FontSize.sm,
    color: Colors.textSecondary,
    marginBottom: 6,
    fontWeight: FontWeight.medium,
  },
  required: { color: Colors.error },
  field: {
    flexDirection: 'row',
    alignItems: 'center',
    borderWidth: 1.5,
    borderColor: Colors.border,
    borderRadius: Radius.md,
    backgroundColor: Colors.surface,
    minHeight: 52,
    paddingHorizontal: Spacing.md,
  },
  fieldFocused: { borderColor: Colors.primary, backgroundColor: '#FFFFFF' },
  fieldError: { borderColor: Colors.error },
  slot: { marginHorizontal: 4 },
  input: {
    flex: 1,
    fontSize: FontSize.md,
    color: Colors.text,
    paddingVertical: 12,
    paddingHorizontal: 4,
  },
  error: { color: Colors.error, fontSize: FontSize.xs, marginTop: 6, fontWeight: FontWeight.medium },
  hint: { color: Colors.textMuted, fontSize: FontSize.xs, marginTop: 6 },
});
