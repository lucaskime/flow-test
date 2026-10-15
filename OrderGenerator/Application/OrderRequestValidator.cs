namespace OrderGenerator.Application;

public static class OrderRequestValidator
{
    public const int MaxQuantityExclusive = 100_000;
    public const decimal MaxPriceExclusive = 1_000m;

    /// <summary>Retorna os erros por campo; vazio quando a ordem é válida.</summary>
    public static IReadOnlyDictionary<string, string> Validate(int quantity, decimal price)
    {
        var errors = new Dictionary<string, string>();

        if (quantity <= 0 || quantity >= MaxQuantityExclusive)
        {
            errors["quantity"] = "Quantidade deve ser um inteiro positivo menor que 100.000.";
        }

        if (price <= 0 || price >= MaxPriceExclusive)
        {
            errors["price"] = "Preço deve ser positivo e menor que 1.000.";
        }
        else if (price % 0.01m != 0)
        {
            errors["price"] = "Preço deve ser múltiplo de 0,01.";
        }

        return errors;
    }
}
