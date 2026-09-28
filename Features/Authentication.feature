Feature: PayPal OAuth2 access token generation
  As an API consumer of the PayPal sandbox
  I want to generate an OAuth2 access token using the client credentials grant
  So that I can authenticate subsequent PayPal API requests

  Background:
    Given the PayPal sandbox API base URL is configured

  Scenario: Successfully generate an access token with valid client credentials
    Given valid PayPal client credentials
    When I request an access token using the client credentials grant type
    Then the response status code should be 200
    And the response should contain a non-empty access token
    And the response should contain a token type of "Bearer"
    And the response should contain a positive token expiry

  Scenario: Fail to generate an access token with invalid client credentials
    Given invalid PayPal client credentials
    When I request an access token using the client credentials grant type
    Then the response status code should be 401
    And the response should contain an authentication error
