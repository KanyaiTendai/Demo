Feature: PayPal order authorization
  As an API consumer of the PayPal sandbox
  I want to authorize a previously created checkout order
  So that I can capture payment once the payer has approved it

  Background:
    Given the PayPal sandbox API base URL is configured
    And a valid PayPal access token has been obtained

  Scenario: Fail to authorize an order that has not been approved by the payer
    Given a PayPal order has been created with intent "AUTHORIZE"
    When I authorize the order
    Then the response status code should be 421
    And the response should contain an order validation error
    And the response should contain a validation error issue of "ORDER_NOT_APPROVED"

  Scenario: Fail to authorize a non-existent order
    Given a non-existent PayPal order id
    When I authorize the order
    Then the response status code should be 404
    And the response should contain an order validation error
    And the response should contain a validation error issue of "INVALID_RESOURCE_ID"

  Scenario: Fail to authorize an order without a valid access token
    Given a PayPal order has been created with intent "AUTHORIZE"
    And an invalid PayPal access token
    When I authorize the order
    Then the response status code should be 401
    And the response should contain an order authentication error