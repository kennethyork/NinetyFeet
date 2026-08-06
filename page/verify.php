<?php
/**
 * Captures a PayPal order and, only if PayPal itself says it was paid in full,
 * hands back a signed download link.
 *
 * The whole point is that this runs on the server. A page that shows the download
 * when PayPal's JavaScript reports success is a page that hands the game to
 * anybody who opens the console and calls the success handler — the browser is
 * the buyer's, not yours, and nothing it tells you about a payment is evidence.
 * So the order id is the only thing that crosses from the page, and it is checked
 * against PayPal's API from here.
 */

declare(strict_types=1);

header('Content-Type: application/json');

$config = require __DIR__ . '/config.php';

function fail(string $why, int $code = 400): never
{
    http_response_code($code);
    echo json_encode(['error' => $why]);
    exit;
}

$body = json_decode(file_get_contents('php://input') ?: '', true);
$orderId  = $body['orderID'] ?? '';
$platform = $body['platform'] ?? '';

if ($orderId === '' || !preg_match('/^[A-Z0-9]{5,32}$/', $orderId)) fail('no order id');
if (!isset($config['files'][$platform])) fail('unknown platform');

// --- An access token, then the capture. -----------------------------------

$ch = curl_init($config['paypal_api'] . '/v1/oauth2/token');
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_USERPWD        => $config['paypal_client_id'] . ':' . $config['paypal_client_secret'],
    CURLOPT_POST           => true,
    CURLOPT_POSTFIELDS     => 'grant_type=client_credentials',
    CURLOPT_TIMEOUT        => 20,
]);
$tokenBody = curl_exec($ch);
$tokenCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
curl_close($ch);

if ($tokenCode !== 200) fail('could not reach PayPal', 502);
$token = json_decode((string) $tokenBody, true)['access_token'] ?? '';
if ($token === '') fail('could not reach PayPal', 502);

// Capture is idempotent enough for this: an order already captured comes back as
// an error naming that, and either way the order is then read back and checked.
$ch = curl_init($config['paypal_api'] . "/v2/checkout/orders/$orderId/capture");
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_POST           => true,
    CURLOPT_POSTFIELDS     => '{}',
    CURLOPT_HTTPHEADER     => ["Authorization: Bearer $token", 'Content-Type: application/json'],
    CURLOPT_TIMEOUT        => 30,
]);
curl_exec($ch);
curl_close($ch);

// Read the order back rather than trusting the capture response, so a replayed or
// already-captured order still has to prove it was actually paid.
$ch = curl_init($config['paypal_api'] . "/v2/checkout/orders/$orderId");
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_HTTPHEADER     => ["Authorization: Bearer $token"],
    CURLOPT_TIMEOUT        => 20,
]);
$orderBody = curl_exec($ch);
$orderCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
curl_close($ch);

if ($orderCode !== 200) fail('could not confirm the payment', 502);
$order = json_decode((string) $orderBody, true);

if (($order['status'] ?? '') !== 'COMPLETED') fail('that order is not paid');

// And it must be paid for *this*, at *this* price. Without these two checks an
// order for a penny, or somebody else's order entirely, buys the game.
$unit = $order['purchase_units'][0] ?? [];
$capture = $unit['payments']['captures'][0] ?? [];

if (($capture['status'] ?? '') !== 'COMPLETED') fail('that order is not paid');
if (($capture['amount']['currency_code'] ?? '') !== $config['currency']) fail('wrong currency');

$paid     = (float) ($capture['amount']['value'] ?? 0);
$expected = (float) $config['price'];
if ($paid + 0.001 < $expected) fail('amount does not match');

// --- A link that expires, tied to one platform and one order. -------------

$expires = time() + (int) $config['link_minutes'] * 60;
$payload = "$platform|$orderId|$expires";
$sig     = hash_hmac('sha256', $payload, $config['link_secret']);

echo json_encode([
    'url' => 'download.php?p=' . rawurlencode($platform)
           . '&o=' . rawurlencode($orderId)
           . '&e=' . $expires
           . '&s=' . $sig,
    'expires_minutes' => (int) $config['link_minutes'],
]);
