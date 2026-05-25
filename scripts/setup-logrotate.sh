#!/bin/bash
# HRNexus — install logrotate config for app logs.
# Run once on the server: bash scripts/setup-logrotate.sh

cat > /etc/logrotate.d/hrnexus << 'EOF'
/var/log/hrnexus-*.log {
    weekly
    rotate 8
    compress
    missingok
    notifempty
    copytruncate
}
EOF

echo "Logrotate config installed at /etc/logrotate.d/hrnexus"
logrotate --debug /etc/logrotate.d/hrnexus
