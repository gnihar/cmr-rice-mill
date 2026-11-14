using Cmr.Contracts;
using Cmr.Api.Services;
using FluentValidation;

namespace Cmr.Api.Validation;

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Pin).NotEmpty().MaximumLength(6);
    }
}

public sealed class LotValidator : AbstractValidator<LotWrite>
{
    public LotValidator()
    {
        RuleFor(x => x.LotNo).NotEmpty().MaximumLength(80);
        RuleFor(x => x.CentreName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.OutturnPct).InclusiveBetween(0.01m, 100m).When(x => x.OutturnPct is not null);
        RuleFor(x => x.PaddyQtyQtl).GreaterThan(0).When(x => x.PaddyQtyQtl is not null);
    }
}

public sealed class PaddyValidator : AbstractValidator<PaddyWrite>
{
    public PaddyValidator()
    {
        RuleFor(x => x.RoNo).NotEmpty().MaximumLength(40);
        RuleFor(x => x.CentreName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.TruckNo).NotEmpty().MaximumLength(20);
        RuleFor(x => x.PaddyQtyQtl).GreaterThan(0);
        RuleFor(x => x.BagsNew).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BagsOld).GreaterThanOrEqualTo(0);
    }
}

public sealed class DoValidator : AbstractValidator<DoWrite>
{
    public DoValidator()
    {
        RuleFor(x => x.DoNo).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Destination).NotEmpty().MaximumLength(160);
        RuleFor(x => x.RiceQtyQtl).GreaterThan(0);
    }
}

public sealed class AcceptanceValidator : AbstractValidator<AcceptanceWrite>
{
    public AcceptanceValidator()
    {
        RuleFor(x => x.CmraNo).NotEmpty().MaximumLength(40);
        RuleFor(x => x.TruckNo).NotEmpty().MaximumLength(20);
        RuleFor(x => x.AcceptedQtyQtl).GreaterThan(0);
        RuleFor(x => x.BagsNew).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BagsOld).GreaterThanOrEqualTo(0);
    }
}

public sealed class WeighmentValidator : AbstractValidator<WeighmentWrite>
{
    public WeighmentValidator()
    {
        RuleFor(x => x.Kind).NotEmpty();
        RuleFor(x => x.TruckNo).NotEmpty();
    }
}

public sealed class SaleValidator : AbstractValidator<ByProductWrite>
{
    public SaleValidator()
    {
        RuleFor(x => x.Product).Must(OfficeService.KnownProduct);
        RuleFor(x => x.Unit).Must(OfficeService.KnownUnit);
        RuleFor(x => x.Qty).GreaterThan(0);
    }
}

public sealed class PriceValidator : AbstractValidator<PriceWrite>
{
    public PriceValidator()
    {
        RuleFor(x => x.PaymentStatus).Must(OfficeService.KnownPayment);
        RuleFor(x => x.Rate).GreaterThanOrEqualTo(0).When(x => x.Rate is not null);
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).When(x => x.Amount is not null);
        RuleFor(x => x.AmountReceived).GreaterThanOrEqualTo(0).When(x => x.AmountReceived is not null);
    }
}

public sealed class ExpenseValidator : AbstractValidator<ExpenseWrite>
{
    public ExpenseValidator()
    {
        RuleFor(x => x.Category).Must(OfficeService.KnownCategory);
        RuleFor(x => x.Mode).Must(OfficeService.KnownMode);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class ReceiptValidator : AbstractValidator<ReceiptWrite>
{
    public ReceiptValidator()
    {
        RuleFor(x => x.Source).Must(OfficeService.KnownSource);
        RuleFor(x => x.Mode).Must(OfficeService.KnownMode);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class SeasonValidator : AbstractValidator<SeasonWrite>
{
    public SeasonValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(40);
        RuleFor(x => x.DefaultOutturnPct).InclusiveBetween(0.01m, 100m).When(x => x.DefaultOutturnPct is not null);
    }
}

public sealed class UserValidator : AbstractValidator<UserWrite>
{
    public UserValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Pin).NotEmpty().MaximumLength(6);
    }
}

public sealed class BagValidator : AbstractValidator<BagWrite>
{
    public BagValidator()
    {
        RuleFor(x => x.Qty).GreaterThan(0);
        RuleFor(x => x).Must(x => OfficeService.KnownBag(x.BagType, x.Direction));
    }
}

public sealed class NoteValidator : AbstractValidator<SendBackRequest>
{
    public NoteValidator()
    {
        RuleFor(x => x.EntityType).NotEmpty();
        RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
    }
}
