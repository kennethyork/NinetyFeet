<?php
// Copy to config.php and fill in. config.php must never be committed or served —
// the secret below can take money in your name.
return [
    // From developer.paypal.com → Apps & Credentials. Live credentials, not sandbox,
    // unless you are testing.
    'paypal_client_id'     => 'REPLACE_ME',
    'paypal_client_secret' => 'REPLACE_ME',

    // 'https://api-m.sandbox.paypal.com' while testing, the live one when selling.
    'paypal_api'           => 'https://api-m.paypal.com',

    'price'    => '19.99',
    'currency' => 'USD',

    // Where the zips actually live. This must be OUTSIDE your web root, or the whole
    // download-gating exercise is decoration — anybody can fetch the file directly.
    // e.g. /home/you/private/ninetyfeet/
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

    // Sales tax, as a percentage, by buyer country — and read the note in RELEASE.md
    // before trusting this. PayPal is not a merchant of record: these rates are yours
    // to set, to keep current, to collect against, and to file. Leave empty to charge
    // the headline price everywhere and settle up yourself.
    //
    //   'GB' => 20.0, 'DE' => 19.0, 'FR' => 20.0,
    'tax_rates' => [],
];
