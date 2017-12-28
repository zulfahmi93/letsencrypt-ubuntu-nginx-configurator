# Automatic Configurator for Let's Encrypt

Automatically obtain SSL certificate from Let's Encrypt and install into nginx server running in Ubuntu. This configurator is using configuration steps provided at [DigitalOcean Tutorial website](https://www.digitalocean.com/community/tutorials/how-to-secure-nginx-with-let-s-encrypt-on-ubuntu-16-04).

## What this configurator will do?

1. Install nginx server if haven't installed.
2. Install letsencrypt if haven't installed.
3. Back up current nginx configuration to HOME folder.
4. Request SSL certificate from Let's Encrypt.
5. Configure nginx to use obtained SSL certificate.
6. Schedule certificate auto renewal using cron.

## Prerequisites

1. Ubuntu OS (tested on Ubuntu Server 16.04).
2. .NET Core (tested on version 2.0.3).

## Remarks

Compile and run this tool in DEBUG mode to obtain test certificate from staging server and run in dry mode.
