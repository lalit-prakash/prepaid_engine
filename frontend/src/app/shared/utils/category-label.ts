import { ConsumerCategory } from '../../core/models/bill.model';

const CATEGORY_LABELS: Record<ConsumerCategory, string> = {
  [ConsumerCategory.Domestic]: 'Domestic',
  [ConsumerCategory.NonDomestic]: 'Non-Domestic (Commercial)',
  [ConsumerCategory.GeneralPurpose]: 'General Purpose',
  [ConsumerCategory.PublicWaterSupply]: 'Public Water Supply / STP',
  [ConsumerCategory.Industrial]: 'Industrial',
  [ConsumerCategory.FerroAlloy]: 'Ferro Alloy',
  [ConsumerCategory.Agriculture]: 'Agriculture',
  [ConsumerCategory.Crematorium]: 'Crematorium',
  [ConsumerCategory.ElectricVehicle]: 'Electric Vehicle Charging',
  [ConsumerCategory.KutirJyotiBpl]: 'BPL / Kutir Jyoti',
  [ConsumerCategory.PublicLighting]: 'Public Lighting (metered)',
};

/** Shared across Billing and Tariffs so the category vocabulary stays in one place. */
export function categoryLabel(category: ConsumerCategory): string {
  return CATEGORY_LABELS[category] ?? 'Unknown';
}

/** Every category, in the order the tariff book lists them. */
export const ALL_CATEGORIES: ConsumerCategory[] = Object.keys(CATEGORY_LABELS).map(Number);
