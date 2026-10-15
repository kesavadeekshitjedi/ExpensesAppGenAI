// Guesses whether a category is taxable from its name. Washington doesn't tax most groceries, so food
// staples default to non-taxable and everything else to taxable. Kept in sync with the API's GuessTaxable
// (CategoryEndpoints). This only pre-fills the suggestion — the user can always override the checkbox.
const NON_TAXABLE_HINTS = [
  'grocer', 'produce', 'vegetable', 'veggie', 'fruit', 'dairy', 'milk', 'bread', 'bakery',
  'meat', 'poultry', 'seafood', 'fish', 'egg', 'food', 'pantry', 'grain', 'rice', 'flour', 'water',
]

export function guessTaxable(name: string): boolean {
  const n = name.toLowerCase()
  return !NON_TAXABLE_HINTS.some((h) => n.includes(h))
}
