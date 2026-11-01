#!/bin/sh
# Provisions all SQS queues (+ dead-letter queues), SNS topics and SNS->SQS subscriptions.
# Idempotent. Works against LocalStack (AWS_ENDPOINT=http://localstack:4566) and, with AWS_ENDPOINT unset, real AWS.
set -eu

REGION="${AWS_DEFAULT_REGION:-us-east-1}"
VISIBILITY="${VISIBILITY_TIMEOUT:-30}"
MAX_RECEIVE="${MAX_RECEIVE_COUNT:-5}"
if [ -n "${AWS_ENDPOINT:-}" ]; then EP="--endpoint-url $AWS_ENDPOINT"; else EP=""; fi
aws_() { aws $EP --region "$REGION" "$@"; }

queue_arn() { aws_ sqs get-queue-attributes --queue-url "$1" --attribute-names QueueArn --query 'Attributes.QueueArn' --output text; }

create_queue_with_dlq() {
  name="$1"
  dlq_url=$(aws_ sqs create-queue --queue-name "${name}-dlq" --query QueueUrl --output text)
  dlq_arn=$(queue_arn "$dlq_url")
  policy="{\"deadLetterTargetArn\":\"${dlq_arn}\",\"maxReceiveCount\":\"${MAX_RECEIVE}\"}"
  # attributes JSON needs RedrivePolicy as an escaped string
  esc=$(printf '%s' "$policy" | sed 's/"/\\"/g')
  aws_ sqs create-queue --queue-name "$name" \
    --attributes "{\"VisibilityTimeout\":\"${VISIBILITY}\",\"RedrivePolicy\":\"${esc}\"}" >/dev/null
  echo "queue ready: $name (+ ${name}-dlq)"
}

echo "Waiting for SQS..."
i=0; until aws_ sqs list-queues >/dev/null 2>&1; do i=$((i+1)); [ $i -gt 60 ] && { echo "SQS not reachable"; exit 1; }; sleep 2; done

for q in order-created-queue payment-result-queue logging-queue notification-queue; do create_queue_with_dlq "$q"; done

order_topic=$(aws_ sns create-topic --name order-events-topic --query TopicArn --output text)
payment_topic=$(aws_ sns create-topic --name payment-events-topic --query TopicArn --output text)
echo "topics ready: $order_topic, $payment_topic"

# payment-events-topic -> payment-result-queue (raw delivery => the SQS body IS the event envelope)
result_url=$(aws_ sqs get-queue-url --queue-name payment-result-queue --query QueueUrl --output text)
result_arn=$(queue_arn "$result_url")
# allow SNS to send to the queue (needed on real AWS; harmless on LocalStack)
pol="{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Principal\":{\"Service\":\"sns.amazonaws.com\"},\"Action\":\"sqs:SendMessage\",\"Resource\":\"${result_arn}\",\"Condition\":{\"ArnEquals\":{\"aws:SourceArn\":\"${payment_topic}\"}}}]}"
esc_pol=$(printf '%s' "$pol" | sed 's/"/\\"/g')
aws_ sqs set-queue-attributes --queue-url "$result_url" --attributes "{\"Policy\":\"${esc_pol}\"}"
aws_ sns subscribe --topic-arn "$payment_topic" --protocol sqs --notification-endpoint "$result_arn" \
  --attributes RawMessageDelivery=true >/dev/null
echo "subscription ready: payment-events-topic -> payment-result-queue"
echo "AWS infrastructure provisioned."
