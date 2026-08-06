<?php
/**
 * Asks Paddle whether a transaction was really completed, and if it was, hands back
 * a signed download link.
 *
 * The whole point is that this runs on the server. A page that shows the download
 * when Paddle's JavaScript reports success is a page that hands the game to anybody
 * who opens the console and calls the success handler — the browser is the buyer's,
 * not yours, and nothing it says about a payment is evidence. So the transaction id
 * is the only thing that crosses from the page, and it is checked against Paddle's
 * API from here.
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
$txnId    = $body['transactionId'] ?? '';
$platform = $body['platform'] ?? '';

if ($txnId === '' || !preg_match('/^txn_[A-Za-z0-9]{10,40}$/', $txnId)) fail('no transaction id');
if (!isset($config['files'][$platform])) fail('unknown platform');

$ch = curl_init(rtrim($config['paddle_api'], '/') . '/transactions/' . rawurlencode($txnId));
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_HTTPHEADER     => [
        'Authorization: Bearer ' . $config['paddle_api_key'],
        'Content-Type: application/json',
    ],
    CURLOPT_TIMEOUT => 25,
]);
$response = curl_exec($ch);
$status   = curl_getinfo($ch, CURLINFO_HTTP_CODE);
curl_close($ch);

if ($status !== 200) fail('could not confirm the payment', 502);

$txn = json_decode((string) $response, true)['data'] ?? null;
if (!is_array($txn)) fail('could not confirm the payment', 502);

// Paddle marks a paid transaction "completed". "billed" means invoiced and not yet
// paid, which is not a sale — handing the game over on that would be handing it over
// on a promise.
if (($txn['status'] ?? '') !== 'completed') fail('that transaction is not paid');

// And it has to be a transaction for *this*. Without this check any completed
// transaction on the account — a different product, a one-cent test — buys the game.
$wanted = $config['prices'][$platform] ?? '';
$bought = false;
foreach ($txn['items'] ?? [] as $item) {
    if (($item['price']['id'] ?? '') === $wanted) { $bought = true; break; }
}

if (!$bought) fail('that transaction is not for this download');

// No amount check here, deliberately. Paddle is the merchant of record: it sets the
// customer's local price, adds their VAT or sales tax and may apply a discount you
// created, so the total legitimately differs from the headline figure. The price id
// is the thing that identifies what was bought, and it is what is checked.

$expires = time() + (int) $config['link_minutes'] * 60;
$payload = "$platform|$txnId|$expires";
$sig     = hash_hmac('sha256', $payload, $config['link_secret']);

echo json_encode([
    'url' => 'download.php?p=' . rawurlencode($platform)
           . '&t=' . rawurlencode($txnId)
           . '&e=' . $expires
           . '&s=' . $sig,
    'expires_minutes' => (int) $config['link_minutes'],
]);
