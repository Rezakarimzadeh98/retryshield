# The failure

A user hits **Pay**. Your server charges the card and commits the order. The response never reaches the phone. The app shows an error. The user taps again.

Without a shared key and a claim-before-side-effect rule, you often get **two charges** or **two orders**.

Timeouts do not mean “nothing happened.”  
Webhooks arriving later do not undo the duplicate your API already created.  
A normal reverse proxy will happily forward the second request.

The same shape appears outside payments:

- booking the same seat twice  
- sending the same SMS twice after a flaky network  
- processing the same Stripe/event webhook twice  
- a queue consumer handling a redelivered message twice  

## What “done right” feels like

1. Every logical operation gets one durable key before the first attempt.  
2. Retries reuse that key and the same payload.  
3. The first owner runs the side effect; later owners get the stored result or a clear conflict.  
4. If you cannot prove the outcome, you stop automatic retries and reconcile.

That is the RetryShield contract—whether you implement it in your app ([kit](../README.md)) or put the [gateway](../../README.md#quick-start) in front of your APIs.
