import { ConsumerCategory } from '../../core/models/bill.model';

const CATEGORY_LABELS: Record<ConsumerCategory, string> = {
  [ConsumerCategory.Domestic]: 'Domestic',
  [ConsumerCategory.Bpl]: 'BPL / Kutir Jyoti',
  [ConsumerCategory.Industrial]: 'Industrial',
  [ConsumerCategory.Commercial]: 'Commercial',
};

/** Shared across Billing and Tariffs so the category vocabulary stays in one place. */
export function categoryLabel(category: ConsumerCategory): string {
  return CATEGORY_LABELS[category] ?? 'Unknown';
}
