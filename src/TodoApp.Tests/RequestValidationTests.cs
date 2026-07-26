using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TodoApp.Shared.Messages;
using TodoApp.WebApi.Controllers;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the declared model-binding constraints that [ApiController] enforces before an
/// action runs: required fields and email format on the shared request records, and the
/// positive-range declarations on route id parameters.
///
/// Email acceptance is defined by [EmailAddress] (a single '@' that is neither first nor last
/// character), which admits inputs the previous MailAddress round-trip rejected — display-name
/// forms and surrounding whitespace; those inputs are pinned here as accepted.
/// </summary>
public class RequestValidationTests
{
    // Runs MVC's own object validator — the same path [ApiController] model binding uses — rather
    // than DataAnnotations' Validator. The two disagree on records: MVC reads constraints from the
    // primary-constructor parameters and throws if they sit on the generated properties, while
    // plain Validator reads only properties and silently misses parameter-targeted attributes.
    private static bool IsValid(object model)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        var provider = services.BuildServiceProvider();

        var actionContext = new ActionContext(
            new DefaultHttpContext { RequestServices = provider },
            new RouteData(),
            new ActionDescriptor());
        provider.GetRequiredService<IObjectModelValidator>()
            .Validate(actionContext, validationState: null, prefix: string.Empty, model: model);
        return actionContext.ModelState.IsValid;
    }

    [Fact]
    public void A_complete_create_user_request_is_valid()
    {
        Assert.True(IsValid(new CreateUserMessage("alice", "a@x.com")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_user_rejects_a_missing_or_blank_username(string? username)
    {
        Assert.False(IsValid(new CreateUserMessage(username!, "a@x.com")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("user@")]
    [InlineData("@x.com")]
    [InlineData("a@@b.com")]
    public void Create_user_rejects_a_missing_or_malformed_email(string? email)
    {
        Assert.False(IsValid(new CreateUserMessage("alice", email!)));
    }

    [Theory]
    [InlineData("Display Name <a@b.com>")]
    [InlineData(" a@b.com")]
    [InlineData("a@b.com ")]
    public void Create_user_accepts_emails_the_former_mailaddress_round_trip_rejected(string email)
    {
        Assert.True(IsValid(new CreateUserMessage("alice", email)));
    }

    [Fact]
    public void Update_user_data_with_no_fields_set_is_valid()
    {
        Assert.True(IsValid(new UpdateUserData()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Update_user_data_rejects_a_malformed_email_when_provided(string email)
    {
        Assert.False(IsValid(new UpdateUserData { Email = email }));
    }

    [Fact]
    public void Update_user_data_accepts_a_well_formed_email()
    {
        Assert.True(IsValid(new UpdateUserData { Email = "a@x.com" }));
    }

    [Fact]
    public void A_complete_create_todo_request_is_valid()
    {
        Assert.True(IsValid(new CreateTodoItemMessage("Buy milk", "2%", 5)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_todo_rejects_a_non_positive_user_id(int userId)
    {
        Assert.False(IsValid(new CreateTodoItemMessage("Buy milk", "2%", userId)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_todo_rejects_a_missing_or_blank_title(string? title)
    {
        Assert.False(IsValid(new CreateTodoItemMessage(title!, "2%", 5)));
    }

    [Fact]
    public void Update_todo_data_with_no_fields_set_is_valid()
    {
        Assert.True(IsValid(new UpdateTodoItemData { IsCompleted = true }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_todo_data_rejects_a_blank_title_when_provided(string title)
    {
        Assert.False(IsValid(new UpdateTodoItemData { Title = title }));
    }

    [Fact]
    public void Update_todo_data_accepts_a_non_blank_title()
    {
        Assert.True(IsValid(new UpdateTodoItemData { Title = "Buy milk" }));
    }

    [Theory]
    [InlineData(typeof(UsersController), nameof(UsersController.GetUserById), "id")]
    [InlineData(typeof(UsersController), nameof(UsersController.UpdateUser), "id")]
    [InlineData(typeof(UsersController), nameof(UsersController.DeleteUser), "id")]
    [InlineData(typeof(TodoItemsController), nameof(TodoItemsController.GetTodoItemById), "id")]
    [InlineData(typeof(TodoItemsController), nameof(TodoItemsController.UpdateTodoItem), "id")]
    [InlineData(typeof(TodoItemsController), nameof(TodoItemsController.DeleteTodoItem), "id")]
    [InlineData(typeof(TodoItemsController), nameof(TodoItemsController.GetTodosByUserId), "userId")]
    public void Route_id_parameters_declare_a_positive_range(Type controller, string action, string parameterName)
    {
        var range = controller.GetMethod(action)!.GetParameters()
            .Single(p => p.Name == parameterName)
            .GetCustomAttribute<RangeAttribute>();

        Assert.NotNull(range);
        Assert.Equal(1, range!.Minimum);
    }
}
