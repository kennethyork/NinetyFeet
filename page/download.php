<?php
/**
 * Streams a purchased build, if the link is one this server issued and has not expired.
 *
 * The zips live outside the web root. If they sit in a public folder then every check
 * in verify.php is theatre — the buyer never needs the link, and neither does anybody
 * else, because the file answers to its own URL.
 */

declare(strict_types=1);

$config = require __DIR__ . '/config.php';

function refuse(string $why, int $code = 403): never
{
    http_response_code($code);
    header('Content-Type: text/plain');
    echo $why;
    exit;
}

$platform = $_GET['p'] ?? '';
$txnId    = $_GET['t'] ?? '';
$expires  = (int) ($_GET['e'] ?? 0);
$sig      = $_GET['s'] ?? '';

if (!isset($config['files'][$platform])) refuse('Unknown download.', 404);

$expected = hash_hmac('sha256', "$platform|$txnId|$expires", $config['link_secret']);

// Constant-time, so the comparison cannot be used to guess a signature a byte at a time.
if (!hash_equals($expected, (string) $sig)) refuse('That link is not valid.');

if (time() > $expires)
    refuse("That link has expired. Reply to your Paddle receipt and I'll send a fresh one.", 410);

$path = rtrim($config['files_dir'], '/') . '/' . $config['files'][$platform];
if (!is_readable($path)) refuse('That file is missing. Please get in touch.', 500);

$name = basename($path);
header('Content-Type: application/zip');
header('Content-Length: ' . filesize($path));
header('Content-Disposition: attachment; filename="' . $name . '"');
header('X-Content-Type-Options: nosniff');
header('Cache-Control: private, no-store');

// Streamed rather than read into memory: these are seventy megabytes and PHP's
// default memory limit is a good deal less than that.
readfile($path);
