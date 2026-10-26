namespace Demo.Performance;

/// <summary>
/// The endpoints a load run can be pointed at. The target decides which safety limits apply.
/// </summary>
public enum PerformanceTarget
{
    /// <summary>POST /v1/oauth2/token.</summary>
    AccessToken,

    /// <summary>POST /v2/checkout/orders.</summary>
    CreateOrder
}
