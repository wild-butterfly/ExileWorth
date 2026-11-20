/** PoE 1 base-item IDs used by the official historical exchange feed.
 * Matched by exact currency name against RePoE base_items.json.
 * https://github.com/brather1ng/RePoE/blob/master/RePoE/data/base_items.json
 */
export const CURRENCY_METADATA: Readonly<Record<string, string>> = {
  chaos: 'Metadata/Items/Currency/CurrencyRerollRare',
  divine: 'Metadata/Items/Currency/CurrencyModValues',
  'veiled-chaos-orb': 'Metadata/Items/Currency/CurrencyRerollRareVeiled',
  'sacred-orb': 'Metadata/Items/Currency/CurrencyRerollDefences',
  annul: 'Metadata/Items/Currency/CurrencyRemoveMod',
  'ancient-orb': 'Metadata/Items/Currency/CurrencyRerollUnique',
  exalted: 'Metadata/Items/Currency/CurrencyAddModToRare',
  'stacked-deck': 'Metadata/Items/DivinationCards/DivinationCardDeck',
  'instilling-orb': 'Metadata/Items/Currency/CurrencyInstillingOrb',
  'enkindling-orb': 'Metadata/Items/Currency/CurrencyEnkindlingOrb',
  'orb-of-unmaking': 'Metadata/Items/Currency/CurrencyAtlasPassiveRefund',
  regal: 'Metadata/Items/Currency/CurrencyUpgradeMagicToRare',
  vaal: 'Metadata/Items/Currency/CurrencyCorrupt',
  gcp: 'Metadata/Items/Currency/CurrencyGemQuality',
  blessed: 'Metadata/Items/Currency/CurrencyRerollImplicit',
  alch: 'Metadata/Items/Currency/CurrencyUpgradeToRare',
  fusing: 'Metadata/Items/Currency/CurrencyRerollSocketLinks',
  bauble: 'Metadata/Items/Currency/CurrencyFlaskQuality',
  regret: 'Metadata/Items/Currency/CurrencyPassiveRefund',
  'orb-of-binding': 'Metadata/Items/Currency/CurrencyUpgradeToRareAndSetSockets',
  chrome: 'Metadata/Items/Currency/CurrencyRerollSocketColours',
  jewellers: 'Metadata/Items/Currency/CurrencyRerollSocketNumbers',
  chance: 'Metadata/Items/Currency/CurrencyUpgradeRandomly',
  scour: 'Metadata/Items/Currency/CurrencyConvertToNormal',
  alt: 'Metadata/Items/Currency/CurrencyRerollMagic',
  aug: 'Metadata/Items/Currency/CurrencyAddModToMagic',
  transmute: 'Metadata/Items/Currency/CurrencyUpgradeToMagic',
};
