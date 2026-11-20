namespace Mapping.Benchmark;
// Pure domain types: no persistence base classes, attributes or references.
public record Address(string Street, string City);
public class Customer { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Name { get; set; } = ""; public string Email { get; set; } = ""; public string Bio { get; set; } = ""; public Address Address { get; set; } = new("", ""); public string DisplayName => Name + " <" + Email + ">"; }
public class CustomerSummary { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Name { get; set; } = ""; }
public class OrderLine { public int Id { get; set; } public string Sku { get; set; } = ""; public int Quantity { get; set; } }
public class Payment { public int Id { get; set; } public decimal Amount { get; set; } }
public class Order { public int Id { get; set; } public string TenantId { get; set; } = ""; public int CustomerId { get; set; } public List<OrderLine> Lines { get; set; } = []; public List<Payment> Payments { get; set; } = []; }
public class Vehicle { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Plate { get; set; } = ""; }
public class Car : Vehicle { public int Doors { get; set; } }
public class Truck : Vehicle { public decimal PayloadKg { get; set; } }
public class Category { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Name { get; set; } = ""; }
public class Product { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Name { get; set; } = ""; public List<Category> Categories { get; set; } = []; }
// Equivalence fixture: separate domain and persistence classes, no inheritance between them.
public class Cust { public int Id { get; set; } public string Name { get; set; } = ""; public string Email { get; set; } = ""; }
public class CustRow { public int CustId { get; set; } public string FullName { get; set; } = ""; public string EmailAddr { get; set; } = ""; }
