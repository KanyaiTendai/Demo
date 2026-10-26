@performance
Feature: PayPal API performance baseline
  As an API consumer of the PayPal sandbox
  I want to measure PayPal response times under a light, controlled load
  So that I can spot performance regressions without breaching PayPal's rate limits or security controls

  Background:
    Given the PayPal sandbox API base URL is configured
    And the performance safety limits are loaded

  Scenario: Access token endpoint responds within the latency budget under sequential load
    Given valid PayPal client credentials
    And a load profile of 3 requests with 1 concurrent user at no more than 0.5 requests per second
    When I run the load profile against the access token endpoint
    Then every request in the load run should have succeeded
    And no request in the load run should have been rate limited
    And the observed request rate should not have exceeded the load profile limit
    And the 95th percentile response time should be below 3000 milliseconds

  Scenario Outline: Order creation responds within the latency budget under light load
    Given a valid PayPal access token has been obtained
    And an order request for 1 "T-Shirt" item priced at "100.00" "USD" with intent "AUTHORIZE"
    And a load profile of <requests> requests with <users> concurrent users at no more than <rate> requests per second
    When I run the load profile against the create order endpoint
    Then every request in the load run should have succeeded
    And no request in the load run should have been rate limited
    And the observed request rate should not have exceeded the load profile limit
    And the average response time should be below <average ms> milliseconds
    And the 95th percentile response time should be below <p95 ms> milliseconds

    Examples:
      | profile    | requests | users | rate | average ms | p95 ms |
      | sequential | 5        | 1     | 0.5  | 2000       | 3000   |
      | concurrent | 10       | 2     | 1    | 2000       | 3000   |

  Scenario: A load profile above the safety limits is rejected before any request is sent
    Given a load profile of 1000 requests with 50 concurrent users at no more than 100 requests per second
    When I check the load profile against the safety limits for the create order endpoint
    Then the load profile should be rejected by the safety limits
    And no request should have been sent to PayPal

  # Known issue: PayPal's sandbox does not deduplicate simultaneous requests that share a
  # PayPal-Request-Id, so this race currently creates 2-3 orders. @ignore skips it in every run
  # (local and CI) without removing it; @known-issue records why. Remove @ignore to run it.
  # See PERFORMANCE_TESTING.md.
  @known-issue @ignore
  Scenario: Simultaneous order requests sharing a PayPal-Request-Id create only one order
    Given a valid PayPal access token has been obtained
    And an order request for 1 "T-Shirt" item priced at "100.00" "USD" with intent "AUTHORIZE"
    And a burst of 3 simultaneous order requests sharing one PayPal-Request-Id
    When I release the burst against the create order endpoint
    And I resend the order request with the same PayPal-Request-Id after the burst
    Then the requests in the burst should have been released within 250 milliseconds of each other
    And no request in the burst should have been rate limited
    And exactly one order should have been created by the burst
    And every request in the burst should have returned that order
    And the resent request should return the same order

  Scenario: A burst above the safety limit is rejected before any request is sent
    Given a burst of 50 simultaneous order requests sharing one PayPal-Request-Id
    When I check the burst against the safety limits
    Then the burst should be rejected by the safety limits
    And no request should have been sent to PayPal
