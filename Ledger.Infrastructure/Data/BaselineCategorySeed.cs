using Ledger.Core.Entities;
using Ledger.Infrastructure.Data;

namespace Ledger.Infrastructure.Data;

/// <summary>
/// Industry-aligned personal finance baseline (YNAB / CFPB / Mint-style groupings).
/// More granular than a spreadsheet row — the web UI can handle the width.
/// </summary>
public static class BaselineCategorySeed
{
    public static void Seed(LedgerDbContext db)
    {
        var groups = new (string Name, bool IsIncome, string[] Categories)[]
        {
            ("Employment Income", true,
            [
                "Salary", "Wages", "Bonus & Commission", "Overtime", "Tips"
            ]),
            ("Other Income", true,
            [
                "Side Hustle", "Rental Income", "Government Benefits", "Tax Refund",
                "Reimbursements", "Gifts Received", "Sold Items", "Other Income"
            ]),
            ("Investment Income", true,
            [
                "Interest", "Dividends", "Capital Gains", "Investment Withdrawal"
            ]),
            ("Housing", false,
            [
                "Rent", "Mortgage", "Property Tax", "Home Insurance", "HOA & Condo Fees",
                "Maintenance & Repairs", "Home Improvement", "Furnishings"
            ]),
            ("Utilities", false,
            [
                "Electric", "Natural Gas", "Water & Sewer", "Trash & Recycling",
                "Internet", "Landline Phone", "Mobile Phone"
            ]),
            ("Food", false,
            [
                "Groceries", "Restaurants & Takeout", "Coffee & Snacks", "Alcohol & Bars"
            ]),
            ("Transportation", false,
            [
                "Fuel", "Auto Payment", "Auto Insurance", "Parking & Tolls",
                "Auto Maintenance & Repairs", "Public Transit", "Rideshare & Taxi"
            ]),
            ("Health & Medical", false,
            [
                "Health Insurance", "Doctor & Hospital", "Dental", "Vision",
                "Pharmacy & Prescriptions", "Mental Health", "Medical Devices"
            ]),
            ("Wellness & Personal Care", false,
            [
                "Fitness & Gym", "Hair & Spa", "Clothing & Shoes", "Toiletries & Cosmetics"
            ]),
            ("Family & Dependents", false,
            [
                "Childcare", "School & Tuition", "Kids Activities & Supplies",
                "Baby & Child Needs", "Elder Care"
            ]),
            ("Pets", false,
            [
                "Pet Food", "Veterinary", "Pet Grooming & Supplies", "Pet Insurance"
            ]),
            ("Home & Household", false,
            [
                "Cleaning Supplies", "Laundry", "Tools & Hardware", "Lawn & Garden"
            ]),
            ("Technology", false,
            [
                "Electronics & Computers", "Software & Cloud Services", "Gadgets & Accessories"
            ]),
            ("Entertainment & Leisure", false,
            [
                "Streaming & Subscriptions", "Movies & Events", "Hobbies & Recreation",
                "Books & Music", "Sports & Games", "Vacation & Travel", "Hotels & Lodging"
            ]),
            ("Gifts & Giving", false,
            [
                "Gifts", "Charitable Donations", "Political Donations"
            ]),
            ("Insurance (Non-Auto/Home)", false,
            [
                "Life Insurance", "Disability Insurance", "Other Insurance"
            ]),
            ("Debt & Financial Fees", false,
            [
                "Credit Card Payment", "Credit Card Interest", "Loan Payment",
                "Bank Fees", "ATM & Cash Withdrawal", "Tax Payment"
            ]),
            ("Savings & Investments", false,
            [
                "Emergency Fund", "Retirement Contribution", "Brokerage Deposit",
                "Education Savings", "Large Purchase Fund"
            ]),
            ("Business & Professional", false,
            [
                "Office Supplies", "Professional Services", "Business Travel",
                "Business Meals", "Education & Training"
            ]),
            ("Miscellaneous", false,
            [
                "Uncategorized", "Miscellaneous", "Lost & Stolen"
            ])
        };

        var sortOrder = 0;
        foreach (var (groupName, isIncome, categories) in groups)
        {
            var group = new CategoryGroup
            {
                Name = groupName,
                IsIncome = isIncome,
                SortOrder = sortOrder++
            };
            db.CategoryGroups.Add(group);

            var catOrder = 0;
            foreach (var catName in categories)
            {
                db.Categories.Add(new Category
                {
                    Name = catName,
                    CategoryGroup = group,
                    IsIncome = isIncome,
                    SortOrder = catOrder++
                });
            }
        }
    }
}
