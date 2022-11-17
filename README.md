# neo4jexporter
Service for monitoring your neo4j community instance with Prometheus

Build with Visual studio 2022

# usage:
just put service file neo4jexporter.service to /etc/system.d/system (ubuntu)
sudo systemctl daemon-reload
sudo service neo4jexporter start

it will create endpoint, which you can add to your Prometheus service

# settings.json file:
port - service port
delay - delay between neo4j querys
neoaddress, neouser, neopassword - neo4j acc
testdelaymultiply - to multiply delay from monitoring queryes to test queryes

# metrics.json file:
name - metric name
description - metric description
query - query to neo4j
count - count of query result recordset (usually 0 or 1) if doesn't match metric will be set to -1 value
istest - true or false - if true query will be executed rarely according to multiplyer

# querylog.txt
- log of all querys to neo4j
