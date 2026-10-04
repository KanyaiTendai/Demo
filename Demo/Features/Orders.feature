Feature: PayPal order creation
  As an API consumer of the PayPal sandbox
  I want to create checkout orders using the Orders v2 API
  So that I can authorize payment for a purchase

  Background:
    Given the PayPal sandbox API base URL is configured

  Scenario: Successfully create an order with valid details
    Given a valid PayPal access token has been obtained
    And an order request for 1 "T-Shirt" item priced at "100.00" "USD" with intent "AUTHORIZE"
    When I create the order
    Then the response status code should be 201
    And the response should contain an order id
    And the response should contain an order status of "CREATED"
    And the response should contain an intent of "AUTHORIZE"
    And the response should contain an "approve" link

  Scenario: Fail to create an order without a valid access token
    Given an invalid PayPal access token
    And an order request for 1 "T-Shirt" item priced at "100.00" "USD" with intent "AUTHORIZE"
    When I create the order
    Then the response status code should be 407
    And the response should contain an order authentication error

  Scenario: Fail to create an order with a missing purchase unit amount
    Given a valid PayPal access token has been obtained
    And an order request for 1 "T-Shirt" item priced at "100.00" "USD" with intent "AUTHORIZE" but no purchase unit amount
    When I create the order
    Then the response status code should be 409
    And the response should contain an order validation error
    And the response should contain a validation error issue of "MISSING_REQUIRED_PARAMETER"