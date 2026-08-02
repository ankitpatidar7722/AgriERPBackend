namespace AgriERP.Shared.Constants;

/// <summary>
/// Mirrors the PermissionCode values seeded into Permissions by
/// 12_SeedData.sql. Controllers reference these constants rather than string
/// literals, so a typo is a compile error instead of an endpoint that silently
/// authorises nobody - which is the dangerous failure, since it looks locked
/// down while actually being unreachable.
///
/// Adding a permission means inserting the row AND adding the constant.
///
/// WHY Item AND ItemSubGroup STILL CARRY "Product." AND "Category." CODES
/// ----------------------------------------------------------------------
/// The classes were renamed with the rest of the Products-to-Items work; the
/// CODES were deliberately left alone. A permission code is an identifier
/// already written into Permissions and into every role's grant list.
/// Renaming it would mean migrating those rows in lockstep, and getting that
/// half-right locks every user out of the screens they had - a failure that
/// looks like an authorisation bug rather than a rename. The codes carry no
/// meaning beyond being unique, so they stay.
/// </summary>
public static class Permissions
{
    public static class Dashboard
    {
        public const string View       = "Dashboard.View";
        public const string ViewProfit = "Dashboard.ViewProfit";
    }

    public static class ItemSubGroup
    {
        public const string View   = "Category.View";
        public const string Create = "Category.Create";
        public const string Edit   = "Category.Edit";
        public const string Delete = "Category.Delete";
    }

    public static class Company
    {
        public const string View   = "Company.View";
        public const string Create = "Company.Create";
        public const string Edit   = "Company.Edit";
        public const string Delete = "Company.Delete";
    }

    public static class Supplier
    {
        public const string View   = "Supplier.View";
        public const string Create = "Supplier.Create";
        public const string Edit   = "Supplier.Edit";
        public const string Delete = "Supplier.Delete";
    }

    public static class Customer
    {
        public const string View   = "Customer.View";
        public const string Create = "Customer.Create";
        public const string Edit   = "Customer.Edit";
        public const string Delete = "Customer.Delete";
    }

    public static class Item
    {
        public const string View     = "Product.View";
        public const string Create   = "Product.Create";
        public const string Edit     = "Product.Edit";
        public const string Delete   = "Product.Delete";
        public const string Import   = "Product.Import";
        public const string Export   = "Product.Export";
        public const string EditRate = "Product.EditRate";
    }

    public static class Stock
    {
        public const string View     = "Stock.View";
        public const string Adjust   = "Stock.Adjust";
        public const string Transfer = "Stock.Transfer";
        public const string Post     = "Stock.Post";
        public const string Opening  = "Stock.Opening";
    }

    public static class Purchase
    {
        public const string View   = "Purchase.View";
        public const string Create = "Purchase.Create";
        public const string Edit   = "Purchase.Edit";
        public const string Post   = "Purchase.Post";
        public const string Cancel = "Purchase.Cancel";
        public const string Return = "Purchase.Return";
        public const string Order  = "Purchase.Order";
    }

    public static class Sales
    {
        public const string View             = "Sales.View";
        public const string Create           = "Sales.Create";
        public const string Edit             = "Sales.Edit";
        public const string Post             = "Sales.Post";
        public const string Cancel           = "Sales.Cancel";
        public const string Return           = "Sales.Return";
        public const string Print            = "Sales.Print";
        public const string Discount         = "Sales.Discount";
        public const string OverrideMinRate  = "Sales.OverrideMinRate";
        public const string CreditSale       = "Sales.CreditSale";
    }

    public static class Payment
    {
        public const string View          = "Payment.View";
        public const string Create        = "Payment.Create";
        public const string Cancel        = "Payment.Cancel";
        public const string ExpenseView   = "Expense.View";
        public const string ExpenseCreate = "Expense.Create";
    }

    public static class Report
    {
        public const string Sales    = "Report.Sales";
        public const string Purchase = "Report.Purchase";
        public const string Stock    = "Report.Stock";
        public const string Profit   = "Report.Profit";
        public const string Gst      = "Report.Gst";
        public const string Party    = "Report.Party";
        public const string Export   = "Report.Export";
    }

    public static class User
    {
        public const string View          = "User.View";
        public const string Create        = "User.Create";
        public const string Edit          = "User.Edit";
        public const string Delete        = "User.Delete";
        public const string ResetPassword = "User.ResetPassword";
        public const string RoleView      = "Role.View";
        public const string RoleManage    = "Role.Manage";
    }

    public static class Settings
    {
        public const string View    = "Settings.View";
        public const string Edit    = "Settings.Edit";
        public const string Audit   = "Settings.Audit";
        public const string YearEnd = "Settings.YearEnd";
    }
}

/// <summary>Claim types the JWT carries beyond the standard set.</summary>
public static class AgriClaimTypes
{
    public const string UserId         = "uid";
    public const string FullName       = "name";
    public const string Role           = "role";
    public const string Permission     = "perm";
    public const string SecurityStamp  = "sstamp";
}
