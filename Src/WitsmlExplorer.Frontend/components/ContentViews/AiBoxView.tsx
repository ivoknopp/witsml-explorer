import { EdsProvider, Switch, TextField, Typography } from "@equinor/eds-core-react";
import styled from "styled-components";
import React, { useState } from "react";
import { AiServiceResult } from "models/logData";
import { CommonPanelContainer } from "components/StyledComponents/Container";
import { UserTheme } from "contexts/operationStateReducer";
import { normaliseThemeForEds } from "tools/themeHelpers";

interface AiBoxViewProps {
    aiServiceResult: AiServiceResult;
    isLoading: boolean;
    isFetching: boolean;
    userQueryTextRef: React.RefObject<HTMLInputElement>;
    refreshData: () => void;
    theme: UserTheme;
}

const AiBoxView = (
    props: AiBoxViewProps
): React.ReactElement => {
    const { isLoading, isFetching, userQueryTextRef, aiServiceResult, refreshData, theme } =
    props;

    const [detailsVisible, setDetailsVisible] = useState<boolean>(false);

    return (
    <AiContentContainer>
        <CommonPanelContainer>
            <StyledTextField
            id="userText"
            disabled={isLoading || isFetching}
            label="Ask question about the data and hit Enter:"
            inputRef={userQueryTextRef}
            onKeyDown={(e: React.KeyboardEvent<HTMLDivElement>) => {
                if (e.key === 'Enter') {
                e.preventDefault(); 
                refreshData()
                }
            }}
            />
          <EdsProvider density={normaliseThemeForEds(theme)}>
            <Switch
              checked={detailsVisible}
              onChange={() => setDetailsVisible(!detailsVisible)}
              size={theme === UserTheme.Compact ? "small" : "default"}
            />
            <Typography style={{ minWidth: "max-content" }}>
              Show execution details
            </Typography>
          </EdsProvider>
        </CommonPanelContainer>
        {aiServiceResult && !aiServiceResult.isSuccess && (
        <Message>
            <Typography>{`Error: ${aiServiceResult?.errorMessage}, FileId:${aiServiceResult.fileId}`}</Typography>
        </Message>          
    )}
    {aiServiceResult && detailsVisible && (
        <Message>
            <Typography variant="h5">AI Service Result:</Typography>
            <Typography>{`FileId: ${aiServiceResult.fileId}`}</Typography>
            <Typography>{`Model Execution Time (miliseconds): ${aiServiceResult.modelExecTimeMs}`}</Typography>
            <Typography>{`Python Execution Time (miliseconds): ${aiServiceResult.pythonExecTimeMs}`}</Typography>
            <Typography>{`Used model: ${aiServiceResult.usedModelName}`}</Typography>
            <Typography style={{ marginTop: '10px' }}>Generated Python Code:</Typography>
            <pre>{aiServiceResult.generatedPythonCode}</pre>
        </Message>          
    )}
    
    </AiContentContainer>     
)}

const StyledTextField = styled(TextField)`
  width: 60% !important;
  margin: 10px;
  align-self: flex-start;
`;

export const AiContentContainer = styled.div`
  display: flex;
  flex-direction: column;
`;

const Message = styled.div`
  margin: 10px;
  padding: 10px;
`;

export default AiBoxView;