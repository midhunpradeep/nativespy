#!/bin/sh
set -eu

EXPECTED='type(scope): description'
PATTERN='^(feat|fix|test|docs|refactor|build|ci|chore)(\([^()[:space:]]+\))?!?:[[:space:]]+[^[:space:]].*$'

usage() {
    printf '%s\n' "Usage: $0 <commit-message-file>" >&2
    printf '%s\n' "       $0 --range <git-revision-range>" >&2
    exit 2
}

validate_subject() {
    subject=$1

    case "$subject" in
        Merge\ *|Revert\ *)
            return 0
            ;;
    esac

    if printf '%s\n' "$subject" | grep -Eq "$PATTERN"; then
        return 0
    fi

    printf 'Invalid commit subject: %s\n' "$subject" >&2
    printf 'Expected Conventional Commit syntax, for example: %s\n' "$EXPECTED" >&2
    printf '%s\n' 'Allowed types: feat, fix, test, docs, refactor, build, ci, chore.' >&2
    return 1
}

validate_message_file() {
    message_file=$1

    if [ ! -f "$message_file" ]; then
        printf 'Commit message file not found: %s\n' "$message_file" >&2
        return 1
    fi

    subject=$(sed -n '1p' "$message_file" | tr -d '\r')
    validate_subject "$subject"
}

validate_range() {
    revision_range=$1
    status=0
    commits=$(git rev-list --no-merges "$revision_range") || {
        printf 'Invalid Git revision range: %s\n' "$revision_range" >&2
        return 1
    }

    for commit in $commits; do
        subject=$(git show -s --format=%s "$commit")
        if ! validate_subject "$subject"; then
            printf 'Commit: %s\n' "$commit" >&2
            status=1
        fi
    done

    return "$status"
}

if [ "$#" -eq 1 ]; then
    validate_message_file "$1"
elif [ "$#" -eq 2 ] && [ "$1" = '--range' ]; then
    validate_range "$2"
else
    usage
fi
