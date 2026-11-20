using Core.DTOs;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;

namespace SadcOrdersAssessment.Helpers
{
    public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
    {
        public CreateCustomerRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(Customer.NameMaxLength);
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(Customer.EmailMaxLength);
            RuleFor(x => x.CountryCode)
                .NotEmpty()
                .Length(2)
                .Must(SadcCurrencyRules.IsSadcCountry)
                .WithMessage("Country must be an SADC member state (ISO 3166-1 alpha-2, e.g. ZA, BW, ZW).")
                .When(x => !string.IsNullOrEmpty(x.CountryCode) && x.CountryCode.Trim().Length == 2, ApplyConditionTo.CurrentValidator);
        }
    }

    public sealed class CreateOrderLineRequestValidator : AbstractValidator<CreateOrderLineRequest>
    {
        public CreateOrderLineRequestValidator()
        {
            RuleFor(x => x.ProductSku)
                .NotEmpty()
                .MaximumLength(OrderLineItem.ProductSkuMaxLength)
                .Matches("^[A-Za-z0-9._-]+$").WithMessage("SKU may contain only letters, digits, '.', '_' and '-'.");
            RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(100_000);
            RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        }
    }

    public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
    {
        public CreateOrderRequestValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.CurrencyCode).NotEmpty().Matches("^[A-Za-z]{3}$").WithMessage("Currency must be a 3-letter ISO 4217 code.");
            RuleFor(x => x.LineItems)
                .NotEmpty().WithMessage("An order needs at least one line item.")
                .Must(items => items is null || items.Count <= Order.MaxLineItems)
                .WithMessage($"An order can have at most {Order.MaxLineItems} line items.");
            RuleForEach(x => x.LineItems).SetValidator(new CreateOrderLineRequestValidator());
        }
    }

    public sealed class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
    {
        public UpdateOrderStatusRequestValidator()
        {
            RuleFor(x => x.Status)
                .NotEmpty()
                .Must(s => StatusParser.TryParser(s, out _))
                .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
        }
    }

    public sealed class CustomerListQueryValidator : AbstractValidator<CustomerListQuery>
    {
        public CustomerListQueryValidator()
        {
            RuleFor(x => x.Search).MaximumLength(200);
            RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
            RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        }
    }

    public sealed class OrderListQueryValidator : AbstractValidator<OrderListQuery>
    {
        public OrderListQueryValidator()
        {
            RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
            RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
            RuleFor(x => x.Status)
                .Must(s => StatusParser.TryParser(s, out _))
                .When(x => !string.IsNullOrEmpty(x.Status))
                .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
            RuleFor(x => x.Sort)
                .Must(s => OrderSort.All.Contains(s!))
                .When(x => !string.IsNullOrEmpty(x.Sort))
                .WithMessage($"Sort must be one of: {string.Join(", ", OrderSort.All)}.");
        }
    }
}