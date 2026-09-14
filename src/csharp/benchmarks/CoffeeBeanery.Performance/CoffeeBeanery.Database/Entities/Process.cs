using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace CoffeeBeanery.Database;

public class Process
{
    [NotMapped] public Schema? Schema { get; set; }

    public DateTime ProcessedDateTime { get; set; }
}

public enum Schema
{
    Banking,
    Lending,
    Accounting
}