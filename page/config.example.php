<?php
// Copy to config.php and fill in. config.php must never be committed or served —
// the API key below can read and act on your Paddle account.
return [
    // Paddle → Developer tools → Authentication. A live key, not sandbox, when selling.
    'paddle_api_key' => 'REPLACE_ME',

    // 'https://sandbox-api.paddle.com' while testing, the live one when selling.
    'paddle_api' => 'https://api.paddle.com',

    // Paddle → Catalog → Products → your price. Looks like pri_01h... One per platform
    // is tidiest, so a receipt says which build was bought; one shared price is fine too.
    'prices' => [
        'windows' => 'pri_REPLACE_ME',
        'linux'   => 'pri_REPLACE_ME',
    ],

    // Where the zips actually live. This must be OUTSIDE your web root, or the whole
    // download-gating exercise is decoration — anybody can fetch the file directly.
    'files_dir' => '/ABSOLUTE/PATH/OUTSIDE/WEBROOT/',

    'files' => [
        'windows' => 'NinetyFeet-windows-x86_64.zip',
        'linux'   => 'NinetyFeet-linux-x86_64.zip',
    ],

    // Any long random string. `openssl rand -hex 32` will do. Changing it invalidates
    // every download link already issued.
    'link_secret' => 'REPLACE_WITH_32_RANDOM_BYTES',

    // How long a download link stays good. Long enough to survive a slow connection
    // and a retry, short enough that a link pasted in a forum is dead by the time
    // anybody reads it.
    'link_minutes' => 120,

    // Nothing about tax here on purpose. Paddle is the merchant of record: it sells to
    // the customer, works out and charges the right VAT or sales tax for wherever they
    // are, and files it. That is the entire reason for choosing it over a plain payment
    // processor, and a rate table in this file would only be a second, wrong answer.
];
